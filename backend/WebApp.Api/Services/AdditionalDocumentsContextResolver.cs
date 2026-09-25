using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using WebApp.Api.Data;
using WebApp.Api.Models;

namespace WebApp.Api.Services;

public sealed record AdditionalDocumentsContext(
    Guid UserId,
    Guid CompanyId,
    Guid MachineId,
    string StripeCustomerId,
    string CompanyName,
    string MachineName,
    string UserEmail,
    string? UserFirstName,
    string? UserLastName,
    string? UserPhone);

public enum AdditionalDocumentsContextError
{
    Forbidden,
    CompanyUnavailable,
    MachineUnavailable,
    BillingAccountMissing,
    StripeCustomerMissing
}

public sealed record AdditionalDocumentsContextResolution(
    AdditionalDocumentsContext? Context,
    AdditionalDocumentsContextError? Error,
    string? ErrorMessage)
{
    public bool Success => Context is not null;

    public static AdditionalDocumentsContextResolution Resolved(AdditionalDocumentsContext context) =>
        new(context, null, null);

    public static AdditionalDocumentsContextResolution Rejected(
        AdditionalDocumentsContextError error,
        string message) => new(null, error, message);
}

/// <summary>
/// Resolves the server-owned company, existing machine and one-off billing identity required by a
/// future AdditionalDocuments request. The caller supplies only a machine id; company and user
/// authority always come from authenticated claims revalidated against SQL.
/// </summary>
public sealed class AdditionalDocumentsContextResolver(
    DiagLinkDbContext db,
    DiagLinkUserLookupService userLookup)
{
    public Task<AdditionalDocumentsContextResolution> ResolveStoredPaymentAsync(
        MachineRequestPayment payment, CancellationToken cancellationToken)
    {
        if (payment.RequestKind != MachineRequestKind.AdditionalDocuments
            || payment.RequestedByUserId is null || payment.CompanyId is null || payment.TargetMachineId is null)
            return Task.FromResult(Forbidden());
        return ResolveAsync(payment.RequestedByUserId.Value, payment.CompanyId.Value,
            payment.TargetMachineId.Value, cancellationToken);
    }

    public async Task<AdditionalDocumentsContextResolution> ResolveAsync(
        ClaimsPrincipal principal,
        Guid machineId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(principal);

        if (!string.Equals(principal.FindFirst(ClaimTypes.Role)?.Value,
                DiagLinkRoles.CompanyAdmin, StringComparison.Ordinal)
            || !Guid.TryParse(principal.FindFirst(DiagLinkClaimTypes.UserId)?.Value, out var claimedUserId)
            || !Guid.TryParse(principal.FindFirst(DiagLinkClaimTypes.CompanyId)?.Value, out var claimedCompanyId))
            return Forbidden();

        return await ResolveAsync(claimedUserId, claimedCompanyId, machineId, cancellationToken);
    }

    private async Task<AdditionalDocumentsContextResolution> ResolveAsync(
        Guid claimedUserId, Guid claimedCompanyId, Guid machineId, CancellationToken cancellationToken)
    {
        var user = await userLookup.FindActiveUserByIdAsync(claimedUserId, cancellationToken);
        if (user is null
            || !string.Equals(user.Role, DiagLinkRoles.CompanyAdmin, StringComparison.Ordinal)
            || user.CompanyId != claimedCompanyId)
            return Forbidden();

        var company = await db.Companies.AsNoTracking().SingleOrDefaultAsync(item =>
            item.Id == user.CompanyId && item.Status == "active", cancellationToken);
        if (company is null)
            return AdditionalDocumentsContextResolution.Rejected(
                AdditionalDocumentsContextError.CompanyUnavailable,
                "L'entreprise n'est pas active.");

        var machine = await db.Machines.AsNoTracking().SingleOrDefaultAsync(item =>
            item.Id == machineId && item.CompanyId == user.CompanyId && item.Status == "active",
            cancellationToken);
        if (machine is null)
            return AdditionalDocumentsContextResolution.Rejected(
                AdditionalDocumentsContextError.MachineUnavailable,
                "La machine n'est pas disponible pour une demande documentaire.");

        var billingAccount = await db.BillingAccounts.AsNoTracking()
            .SingleOrDefaultAsync(account => account.CompanyId == user.CompanyId, cancellationToken);
        if (billingAccount is null)
            return AdditionalDocumentsContextResolution.Rejected(
                AdditionalDocumentsContextError.BillingAccountMissing,
                "Le compte de facturation de l'entreprise est introuvable.");
        if (string.IsNullOrWhiteSpace(billingAccount.StripeCustomerId))
            return AdditionalDocumentsContextResolution.Rejected(
                AdditionalDocumentsContextError.StripeCustomerMissing,
                "Le Customer Stripe de l'entreprise n'est pas configuré.");

        return AdditionalDocumentsContextResolution.Resolved(new AdditionalDocumentsContext(
            user.Id,
            company.Id,
            machine.Id,
            billingAccount.StripeCustomerId,
            company.Name,
            machine.Name,
            user.Email,
            user.FirstName,
            user.LastName,
            user.PhoneNumber));
    }

    private static AdditionalDocumentsContextResolution Forbidden() =>
        AdditionalDocumentsContextResolution.Rejected(
            AdditionalDocumentsContextError.Forbidden,
            "Cet utilisateur n'est pas autorisé à demander l'ajout de documents.");
}
