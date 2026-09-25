using Microsoft.EntityFrameworkCore;
using Stripe;
using WebApp.Api.Data;
using WebApp.Api.Models;

namespace WebApp.Api.Services;

public sealed record MachineRequestSubscriptionContext(string CustomerId, string PaymentMethodId,
    string? SubscriptionId, int TargetQuantity);

public interface IMachineRequestSubscriptionGateway
{
    Task ValidatePriceAsync(CancellationToken ct);
    Task<MachineRequestSubscriptionContext> ReadContextAsync(MachineRequestPayment payment,
        string customerId, string? subscriptionId, int targetQuantity, CancellationToken ct);
    Task<MachineRequestSubscriptionContext> ReadAdditionalContextAsync(MachineRequestPayment payment,
        string customerId, string subscriptionId, int targetQuantity, CancellationToken ct) =>
        ReadContextAsync(payment, customerId, subscriptionId, targetQuantity, ct);
    Task<StripeSubscriptionSnapshot> CreateAsync(MachineRequestPayment payment,
        MachineRequestSubscriptionContext context, DateTime firstPeriodEndUtc, CancellationToken ct);
    Task<StripeSubscriptionSnapshot> ReadAsync(MachineRequestPayment payment,
        MachineRequestSubscriptionContext context, CancellationToken ct);
    Task<StripeSubscriptionSnapshot> SetQuantityAsync(MachineRequestPayment payment,
        MachineRequestSubscriptionContext context, CancellationToken ct);
}

public sealed class StripeMachineRequestSubscriptionGateway : IMachineRequestSubscriptionGateway
{
    private readonly StripeBillingOptions options;
    private readonly PriceService prices; private readonly CustomerService customers;
    private readonly PaymentIntentService intents; private readonly PaymentMethodService methods;
    private readonly SubscriptionService subscriptions; private readonly SubscriptionItemService items;
    public StripeMachineRequestSubscriptionGateway(StripeBillingOptions options) : this(options, new StripeClient(options.SecretKey)) { }
    public StripeMachineRequestSubscriptionGateway(StripeBillingOptions options, IStripeClient client)
    { this.options = options; prices = new(client); customers = new(client); intents = new(client); methods = new(client); subscriptions = new(client); items = new(client); }

    public async Task ValidatePriceAsync(CancellationToken ct)
    {
        options.Validate(); var price = await prices.GetAsync(options.PriceId, cancellationToken: ct);
        if (!price.Active || price.Livemode || price.Currency != "eur" || price.UnitAmount != 2990
            || price.Type != "recurring" || price.Recurring?.Interval != "month" || price.Recurring.IntervalCount != 1)
            throw new InvalidOperationException("Le Price Stripe doit être le tarif test actif de 29,90 EUR par mois.");
    }

    public async Task<MachineRequestSubscriptionContext> ReadContextAsync(MachineRequestPayment payment,
        string customerId, string? subscriptionId, int targetQuantity, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(payment.StripePaymentIntentId)) throw new InvalidOperationException("PaymentIntent Stripe absent.");
        var intent = await intents.GetAsync(payment.StripePaymentIntentId, cancellationToken: ct);
        if (intent.Livemode || intent.Status != "succeeded" || intent.CustomerId != customerId
            || string.IsNullOrEmpty(intent.PaymentMethodId)) throw new InvalidOperationException("Le paiement Stripe initial est incohérent.");
        var method = await methods.GetAsync(intent.PaymentMethodId, cancellationToken: ct);
        var customer = await customers.GetAsync(customerId, cancellationToken: ct);
        if (method.Livemode || method.CustomerId != customerId || customer.Deleted == true
            || customer.InvoiceSettings?.DefaultPaymentMethodId != method.Id)
            throw new InvalidOperationException("Le Customer ou son moyen de paiement par défaut est incohérent.");
        return new(customerId, method.Id, subscriptionId, targetQuantity);
    }

    public async Task<MachineRequestSubscriptionContext> ReadAdditionalContextAsync(MachineRequestPayment payment,
        string customerId, string subscriptionId, int targetQuantity, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(payment.StripePaymentIntentId))
            throw new InvalidOperationException("PaymentIntent Stripe absent.");
        var intent = await intents.GetAsync(payment.StripePaymentIntentId, cancellationToken: ct);
        if (intent.Livemode || intent.Status != "succeeded" || intent.CustomerId != customerId)
            throw new InvalidOperationException("Le paiement Stripe initial n'appartient pas au Customer attendu.");
        var customer = await customers.GetAsync(customerId, cancellationToken: ct);
        if (customer.Deleted == true || customer.Livemode)
            throw new InvalidOperationException("Le Customer Stripe existant est invalide.");
        var subscription = await subscriptions.GetAsync(subscriptionId, cancellationToken: ct);
        if (subscription.CustomerId != customerId || subscription.CollectionMethod != "charge_automatically"
            || subscription.Metadata?.GetValueOrDefault("diaglink_company_id") != payment.CompanyId?.ToString())
            throw new InvalidOperationException("La Subscription Stripe existante est incohérente.");
        var paymentMethodId = subscription.DefaultPaymentMethodId
            ?? customer.InvoiceSettings?.DefaultPaymentMethodId;
        if (string.IsNullOrWhiteSpace(paymentMethodId))
            throw new InvalidOperationException("Aucun moyen de paiement récurrent n'est configuré.");
        var method = await methods.GetAsync(paymentMethodId, cancellationToken: ct);
        if (method.Livemode || method.CustomerId != customerId)
            throw new InvalidOperationException("Le moyen de paiement récurrent est incohérent.");
        var context = new MachineRequestSubscriptionContext(customerId, paymentMethodId,
            subscriptionId, targetQuantity);
        _ = Snapshot(subscription, context, payment.FirstPeriodEndUtc!.Value, false);
        return context;
    }

    public async Task<StripeSubscriptionSnapshot> CreateAsync(MachineRequestPayment payment,
        MachineRequestSubscriptionContext context, DateTime firstPeriodEndUtc, CancellationToken ct)
    {
        var matches = new List<Subscription>();
        await foreach (var candidate in subscriptions.ListAutoPagingAsync(new SubscriptionListOptions
        { Customer = context.CustomerId, Status = "all", Limit = 100 }, cancellationToken: ct))
            if (candidate.Metadata?.GetValueOrDefault("diaglink_machine_request_payment_id") == payment.PaymentRequestId.ToString()) matches.Add(candidate);
        if (matches.Count > 1) throw new InvalidOperationException("Plusieurs Subscriptions Stripe correspondent à cette demande.");
        var subscription = matches.SingleOrDefault();
        if (subscription is null)
        {
            subscription = await subscriptions.CreateAsync(new SubscriptionCreateOptions
            {
                Customer = context.CustomerId, DefaultPaymentMethod = context.PaymentMethodId,
                Items = [new SubscriptionItemOptions { Price = options.PriceId, Quantity = context.TargetQuantity }],
                CollectionMethod = "charge_automatically", TrialEnd = firstPeriodEndUtc,
                ProrationBehavior = "none",
                Metadata = new() { ["diaglink_company_id"] = payment.CompanyId!.Value.ToString(),
                    ["diaglink_machine_request_payment_id"] = payment.PaymentRequestId.ToString(),
                    ["diaglink_machine_id"] = payment.MachineId!.Value.ToString() }
            }, Key(payment, "subscription:v2"), ct);
        }
        return Snapshot(subscription, context, firstPeriodEndUtc, requireTrialBoundary: true);
    }

    public async Task<StripeSubscriptionSnapshot> ReadAsync(MachineRequestPayment payment,
        MachineRequestSubscriptionContext context, CancellationToken ct) => Snapshot(
        await subscriptions.GetAsync(context.SubscriptionId, cancellationToken: ct), context,
        payment.FirstPeriodEndUtc!.Value, requireTrialBoundary: false);

    public async Task<StripeSubscriptionSnapshot> SetQuantityAsync(MachineRequestPayment payment,
        MachineRequestSubscriptionContext context, CancellationToken ct)
    {
        var before = await subscriptions.GetAsync(context.SubscriptionId, cancellationToken: ct);
        var snapshot = Snapshot(before, context, payment.FirstPeriodEndUtc!.Value, false);
        if (snapshot.Quantity == context.TargetQuantity) return snapshot;
        if (snapshot.Quantity != context.TargetQuantity - 1)
            throw new InvalidOperationException("La quantité Stripe ne correspond pas à l'état facturable attendu.");
        var item = await items.UpdateAsync(snapshot.ItemId, new SubscriptionItemUpdateOptions
        { Quantity = context.TargetQuantity, ProrationBehavior = "none" }, Key(payment, "quantity"), ct);
        if (item.Quantity != context.TargetQuantity) throw new InvalidOperationException("La quantité Stripe mise à jour n'est pas confirmée.");
        return Snapshot(await subscriptions.GetAsync(context.SubscriptionId, cancellationToken: ct), context,
            payment.FirstPeriodEndUtc.Value, false);
    }

    private StripeSubscriptionSnapshot Snapshot(Subscription subscription, MachineRequestSubscriptionContext context,
        DateTime boundary, bool requireTrialBoundary)
    {
        if (subscription.Livemode || subscription.CustomerId != context.CustomerId || subscription.Items?.HasMore != false
            || subscription.Items.Data.Count != 1 || subscription.Items.Data[0].Price?.Id != options.PriceId
            || (!string.IsNullOrEmpty(subscription.DefaultPaymentMethodId) && subscription.DefaultPaymentMethodId != context.PaymentMethodId)
            || (requireTrialBoundary && subscription.DefaultPaymentMethodId != context.PaymentMethodId)
            || subscription.Status is not ("active" or "trialing"))
            throw new InvalidOperationException("La Subscription Stripe est incohérente.");
        var item = subscription.Items.Data[0];
        if (item.CurrentPeriodEnd != boundary || (requireTrialBoundary && (subscription.TrialEnd != boundary
            || subscription.BillingCycleAnchor != boundary || subscription.Status != "trialing")))
            throw new InvalidOperationException("Le cycle Stripe ne correspond pas à FirstPeriodEndUtc.");
        return new(subscription.Id, context.CustomerId, subscription.Status,
            DateTime.SpecifyKind(item.CurrentPeriodStart, DateTimeKind.Utc), DateTime.SpecifyKind(item.CurrentPeriodEnd, DateTimeKind.Utc), item.Quantity)
            { ItemId = item.Id };
    }
    private static RequestOptions Key(MachineRequestPayment payment, string step) =>
        new() { IdempotencyKey = $"diaglink:machine-request-payment:{payment.PaymentRequestId:N}:{step}" };
}

public sealed class MachineRequestSubscriptionService(DbContextOptions<DiagLinkDbContext> options,
    MachineRequestPaymentStore store, StripeBillingService billing, IMachineRequestSubscriptionGateway gateway,
    AdditionalMachinePaymentContextResolver additionalContextResolver)
{
    public async Task<MachineRequestPayment?> ConfigureAsync(Guid id, CancellationToken ct)
    {
        var payment = await store.GetAsync(id, ct); if (payment is null) return null;
        if (payment.Status != "captured" || payment.CompanyId is null || payment.MachineId is null
            || payment.FirstPeriodEndUtc is null || payment.ActivatedAtUtc is null
            || payment.ServiceAmountCents is null || payment.FinalCaptureAmountCents is null
            || payment.ProvisioningStage < Models.Entities.MachineRequestProvisioningStage.CustomerLinked)
            throw new InvalidOperationException("Le paiement capturé et le Customer rattaché sont requis.");
        await gateway.ValidatePriceAsync(ct);
        string customerId; string? subscriptionId; int activeCount;
        await using (var db = new DiagLinkDbContext(options))
        {
            var account = await db.BillingAccounts.AsNoTracking().SingleOrDefaultAsync(a => a.CompanyId == payment.CompanyId, ct)
                ?? throw new InvalidOperationException("BillingAccount absent.");
            customerId = account.StripeCustomerId ?? throw new InvalidOperationException("Customer Stripe absent du BillingAccount.");
            subscriptionId = account.StripeSubscriptionId;
            activeCount = await db.Machines.CountAsync(m => m.CompanyId == payment.CompanyId && m.Status == "active", ct);
        }
        if (activeCount <= 0) throw new InvalidOperationException("Aucune machine active à facturer.");
        var isAdditional = payment.RequestKind == MachineRequestKind.AdditionalMachine;
        if (isAdditional)
        {
            var resolution = await additionalContextResolver.ResolveStoredPaymentAsync(payment, ct);
            var additional = resolution.Context ?? throw new InvalidOperationException(resolution.ErrorMessage);
            if (additional.StripeCustomerId != customerId || additional.StripeSubscriptionId != subscriptionId)
                throw new InvalidOperationException("Le contexte Stripe AdditionalMachine est incohérent.");
            if (subscriptionId is null)
                throw new InvalidOperationException("Subscription Stripe existante obligatoire.");
        }
        if (!isAdditional && subscriptionId is null && activeCount != 1)
            throw new InvalidOperationException("Une première Subscription ne peut être créée que pour une entreprise ayant exactement une machine active.");
        var context = isAdditional
            ? await gateway.ReadAdditionalContextAsync(payment, customerId, subscriptionId!, activeCount, ct)
            : await gateway.ReadContextAsync(payment, customerId, subscriptionId, activeCount, ct);
        var subscription = !isAdditional && subscriptionId is null
            ? await gateway.CreateAsync(payment, context, payment.FirstPeriodEndUtc.Value, ct)
            : await gateway.SetQuantityAsync(payment, context, ct);
        if (subscription.Quantity != activeCount) throw new InvalidOperationException("La quantité finale Stripe est incohérente.");
        await billing.LinkExistingSubscriptionAsync(payment.CompanyId.Value, subscription, ct);
        return await store.MarkSubscriptionCreatedAsync(payment, subscription.Id, ct);
    }
}
