using Microsoft.EntityFrameworkCore;
using WebApp.Api.Data;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;

namespace WebApp.Api.Services;

public sealed class MachineRequestActivationService(DbContextOptions<DiagLinkDbContext> options,
    MachineRequestPaymentStore store, MachineBillingPeriodService periods,
    IMachineRequestSubscriptionGateway stripe, TimeProvider timeProvider)
{
    public async Task<MachineRequestPayment?> ActivateAsync(Guid id, CancellationToken ct)
    {
        var payment = await store.GetAsync(id, ct); if (payment is null) return null;
        if (payment.Status != "captured" || payment.CompanyId is null || payment.MachineId is null
            || payment.ActivatedAtUtc is null || payment.FirstPeriodEndUtc is null
            || payment.ServiceAmountCents is null || payment.FinalCaptureAmountCents is null
            || payment.ActivatedAtUtc >= payment.FirstPeriodEndUtc
            || payment.ProvisioningStage < MachineRequestProvisioningStage.SubscriptionCreated)
            throw new InvalidOperationException("Le paiement capturé et l'abonnement configuré sont requis.");

        string customerId; string subscriptionId; int activeCount;
        await using (var db = new DiagLinkDbContext(options))
        {
            if (!await db.Companies.AsNoTracking().AnyAsync(c => c.Id == payment.CompanyId && c.Status == "active", ct))
                throw new InvalidOperationException("L'entreprise active est introuvable.");
            var machine = await db.Machines.AsNoTracking().SingleOrDefaultAsync(m => m.Id == payment.MachineId, ct)
                ?? throw new InvalidOperationException("La machine est introuvable.");
            if (machine.CompanyId != payment.CompanyId || machine.Status != "active")
                throw new InvalidOperationException("La machine n'est pas active dans l'entreprise attendue.");
            var account = await db.BillingAccounts.AsNoTracking().SingleOrDefaultAsync(a => a.CompanyId == payment.CompanyId, ct)
                ?? throw new InvalidOperationException("BillingAccount absent.");
            customerId = account.StripeCustomerId ?? throw new InvalidOperationException("Customer Stripe absent.");
            subscriptionId = account.StripeSubscriptionId ?? throw new InvalidOperationException("Subscription Stripe absente.");
            activeCount = await db.Machines.CountAsync(m => m.CompanyId == payment.CompanyId && m.Status == "active", ct);
        }

        await stripe.ValidatePriceAsync(ct);
        var context = payment.RequestKind == MachineRequestKind.AdditionalMachine
            ? await stripe.ReadAdditionalContextAsync(payment, customerId, subscriptionId, activeCount, ct)
            : await stripe.ReadContextAsync(payment, customerId, subscriptionId, activeCount, ct);
        var subscription = await stripe.ReadAsync(payment, context, ct);
        if (subscription.Id != subscriptionId || subscription.Quantity != activeCount)
            throw new InvalidOperationException("La Subscription Stripe ne couvre pas les machines actives attendues.");

        var created = await periods.CreateInitialPeriodAsync(payment.MachineId.Value,
            payment.ActivatedAtUtc.Value, payment.ActivatedAtUtc.Value, payment.FirstPeriodEndUtc.Value,
            ct, payment.CompanyId.Value);
        if (!created.Success) throw new InvalidOperationException("La période initiale ne peut pas être créée : " + created.Status);
        await ValidatePeriodAsync(payment, ct);

        if (payment.ProvisioningStage < MachineRequestProvisioningStage.InitialPeriodCreated)
            payment = await store.MarkInitialPeriodCreatedAsync(payment, ct);
        if (payment.ProvisioningStage < MachineRequestProvisioningStage.Completed)
            payment = await store.CompleteProvisioningAsync(payment, timeProvider.GetUtcNow().UtcDateTime, ct);
        await ValidatePeriodAsync(payment, ct);
        return payment;
    }

    private async Task ValidatePeriodAsync(MachineRequestPayment payment, CancellationToken ct)
    {
        await using var db = new DiagLinkDbContext(options);
        var matches = await db.MachineBillingPeriods.AsNoTracking().Where(p => p.MachineId == payment.MachineId
            && p.PeriodStartUtc == payment.ActivatedAtUtc).ToListAsync(ct);
        if (matches.Count != 1) throw new InvalidOperationException("La période initiale est absente ou ambiguë.");
        var period = matches[0];
        if (period.PeriodEndUtc != payment.FirstPeriodEndUtc || period.IncludedAiBudgetRealCost != 10m
            || period.IncludedAiUsedRealCost < 0m || period.IncludedAiUsedRealCost > period.IncludedAiBudgetRealCost
            || period.Status is not ("Active" or "Closed"))
            throw new InvalidOperationException("La période initiale existante est incohérente.");
    }
}
