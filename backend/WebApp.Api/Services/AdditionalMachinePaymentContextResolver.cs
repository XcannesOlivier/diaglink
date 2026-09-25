using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using WebApp.Api.Data;
using WebApp.Api.Models;

namespace WebApp.Api.Services;

public sealed record AdditionalMachinePaymentContext(
    Guid UserId,
    Guid CompanyId,
    string StripeCustomerId,
    string StripeSubscriptionId,
    string CompanyName = "",
    string UserEmail = "",
    string? UserFirstName = null,
    string? UserLastName = null,
    string? UserPhone = null);

public enum AdditionalMachinePaymentContextError
{
    Forbidden,
    CompanyUnavailable,
    BillingAccountMissing,
    StripeCustomerMissing,
    StripeSubscriptionMissing
}

public sealed record AdditionalMachinePaymentContextResolution(
    AdditionalMachinePaymentContext? Context,
    AdditionalMachinePaymentContextError? Error,
    string? ErrorMessage)
{
    public bool Success => Context is not null;

    public static AdditionalMachinePaymentContextResolution Resolved(AdditionalMachinePaymentContext context) =>
        new(context, null, null);

    public static AdditionalMachinePaymentContextResolution Rejected(
        AdditionalMachinePaymentContextError error,
        string message) => new(null, error, message);
}

/// <summary>
/// Resolves the server-owned identity and billing context required by a future additional-machine
/// payment. It performs read-only SQL validation and accepts no company or Stripe identifier from
/// the caller.
/// </summary>
public sealed class AdditionalMachinePaymentContextResolver(
    DiagLinkDbContext db,
    DiagLinkUserLookupService userLookup)
{
    public async Task<AdditionalMachinePaymentContextResolution> ResolveAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var role = principal.FindFirst(ClaimTypes.Role)?.Value;
        if (!string.Equals(role, DiagLinkRoles.CompanyAdmin, StringComparison.Ordinal))
            return Forbidden();

        if (!Guid.TryParse(principal.FindFirst(DiagLinkClaimTypes.UserId)?.Value, out var claimedUserId)
            || !Guid.TryParse(principal.FindFirst(DiagLinkClaimTypes.CompanyId)?.Value, out var claimedCompanyId))
            return Forbidden();

        return await ResolveAsync(claimedUserId, claimedCompanyId, cancellationToken);
    }

    public Task<AdditionalMachinePaymentContextResolution> ResolveStoredPaymentAsync(
        MachineRequestPayment payment, CancellationToken cancellationToken)
    {
        if (payment.RequestKind != MachineRequestKind.AdditionalMachine
            || payment.RequestedByUserId is null || payment.CompanyId is null)
            return Task.FromResult(Forbidden());
        return ResolveAsync(payment.RequestedByUserId.Value, payment.CompanyId.Value, cancellationToken);
    }

    private async Task<AdditionalMachinePaymentContextResolution> ResolveAsync(
        Guid claimedUserId, Guid claimedCompanyId, CancellationToken cancellationToken)
    {
        var user = await userLookup.FindActiveUserByIdAsync(claimedUserId, cancellationToken);
        if (user is null
            || !string.Equals(user.Role, DiagLinkRoles.CompanyAdmin, StringComparison.Ordinal)
            || user.CompanyId != claimedCompanyId)
            return Forbidden();

        var company = await db.Companies.AsNoTracking().SingleOrDefaultAsync(company =>
            company.Id == user.CompanyId && company.Status == "active", cancellationToken);
        if (company is null)
            return AdditionalMachinePaymentContextResolution.Rejected(
                AdditionalMachinePaymentContextError.CompanyUnavailable,
                "L'entreprise n'est pas active.");

        var billingAccount = await db.BillingAccounts.AsNoTracking()
            .SingleOrDefaultAsync(account => account.CompanyId == user.CompanyId, cancellationToken);
        if (billingAccount is null)
            return AdditionalMachinePaymentContextResolution.Rejected(
                AdditionalMachinePaymentContextError.BillingAccountMissing,
                "Le compte de facturation de l'entreprise est introuvable.");
        if (string.IsNullOrWhiteSpace(billingAccount.StripeCustomerId))
            return AdditionalMachinePaymentContextResolution.Rejected(
                AdditionalMachinePaymentContextError.StripeCustomerMissing,
                "Le Customer Stripe de l'entreprise n'est pas configuré.");
        if (string.IsNullOrWhiteSpace(billingAccount.StripeSubscriptionId))
            return AdditionalMachinePaymentContextResolution.Rejected(
                AdditionalMachinePaymentContextError.StripeSubscriptionMissing,
                "L'abonnement Stripe de l'entreprise n'est pas configuré.");

        return AdditionalMachinePaymentContextResolution.Resolved(new AdditionalMachinePaymentContext(
            user.Id,
            user.CompanyId,
            billingAccount.StripeCustomerId,
            billingAccount.StripeSubscriptionId,
            company.Name,
            user.Email,
            user.FirstName,
            user.LastName,
            user.PhoneNumber));
    }

    private static AdditionalMachinePaymentContextResolution Forbidden() =>
        AdditionalMachinePaymentContextResolution.Rejected(
            AdditionalMachinePaymentContextError.Forbidden,
            "Cet utilisateur n'est pas autorisé à demander une machine supplémentaire.");
}
