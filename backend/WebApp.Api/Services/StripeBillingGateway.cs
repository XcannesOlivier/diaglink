using Stripe;

namespace WebApp.Api.Services;

/// <summary>Only creates customers/subscriptions; never products, prices or payment methods.</summary>
public sealed partial class StripeBillingGateway : IStripeBillingGateway
{
    private readonly StripeBillingOptions options;
    private readonly Stripe.TestHelpers.TestClockService testClocks;
    private readonly EventService events;
    private readonly CustomerService customers;
    private readonly SubscriptionService subscriptions;
    private readonly PriceService prices;
    private readonly InvoiceService invoices;
    private readonly InvoiceItemService invoiceItems;
    private readonly InvoicePaymentService invoicePayments;
    private readonly SubscriptionItemService subscriptionItems;

    public StripeBillingGateway(StripeBillingOptions options) : this(options, new StripeClient(options.SecretKey)) { }

    public StripeBillingGateway(StripeBillingOptions options, IStripeClient client)
    {
        this.options = options;
        testClocks = new(client); events = new(client);
        customers = new CustomerService(client);
        subscriptions = new SubscriptionService(client);
        prices = new PriceService(client);
        invoices = new InvoiceService(client);
        invoiceItems = new InvoiceItemService(client);
        invoicePayments = new InvoicePaymentService(client);
        subscriptionItems = new SubscriptionItemService(client);
    }

    public async Task ValidatePriceAsync(CancellationToken ct)
    {
        options.Validate();
        var price = await prices.GetAsync(options.PriceId, cancellationToken: ct);
        if (!price.Active || price.Currency != "eur" || price.UnitAmount != 2990
            || price.UnitAmountDecimal != 2990m || price.Type != "recurring"
            || price.Recurring?.Interval != "month" || price.Recurring.IntervalCount != 1
            || price.Recurring.UsageType != "licensed" || price.BillingScheme != "per_unit"
            || price.TransformQuantity != null || price.TaxBehavior != "exclusive"
            || price.Livemode != (options.SecretKey.StartsWith("sk_live_", StringComparison.Ordinal)
                || options.SecretKey.StartsWith("rk_live_", StringComparison.Ordinal)))
            throw new InvalidOperationException("Stripe price must be active, EUR 29.90 per unit/month, licensed, tax exclusive, and match the key mode.");
    }

    public async Task<string> CreateCustomerAsync(Guid companyId, Guid accountId, CancellationToken ct)
    {
        options.Validate();
        var customer = await customers.CreateAsync(new CustomerCreateOptions
        {
            // Stable parameters even if the company name changes during a retry.
            Metadata = new() { ["diaglink_company_id"] = companyId.ToString() }
        }, new RequestOptions { IdempotencyKey = $"diaglink:customer:{accountId:N}" }, ct);
        return CheckCustomer(customer, companyId);
    }

    public async Task<string> GetCustomerAsync(string customerId, Guid companyId, CancellationToken ct)
    {
        options.Validate();
        return CheckCustomer(await customers.GetAsync(customerId, cancellationToken: ct), companyId);
    }

    private static string CheckCustomer(Customer customer, Guid companyId)
    {
        if (customer.Deleted == true || customer.Metadata?.GetValueOrDefault("diaglink_company_id") != companyId.ToString())
            throw new InvalidOperationException("Stripe customer ownership mismatch or customer deleted.");
        return customer.Id;
    }

    public async Task<StripeSubscriptionSnapshot> CreateSubscriptionAsync(string customerId, Guid companyId,
        Guid accountId, int quantity, CancellationToken ct)
    {
        options.Validate();
        if (quantity <= 0) throw new InvalidOperationException("No billable machines.");
        var subscription = await subscriptions.CreateAsync(new SubscriptionCreateOptions
        {
            Customer = customerId,
            Items = [new SubscriptionItemOptions { Price = options.PriceId, Quantity = quantity }],
            CollectionMethod = "charge_automatically", PaymentBehavior = "default_incomplete",
            PaymentSettings = new SubscriptionPaymentSettingsOptions { SaveDefaultPaymentMethod = "on_subscription" },
            Metadata = new() { ["diaglink_company_id"] = companyId.ToString() }
        }, new RequestOptions { IdempotencyKey = $"diaglink:subscription:{accountId:N}" }, ct);
        return Snapshot(subscription, customerId, companyId);
    }

    public async Task<StripeSubscriptionSnapshot> GetSubscriptionAsync(string subscriptionId, string customerId,
        Guid companyId, CancellationToken ct)
    {
        options.Validate();
        return Snapshot(await subscriptions.GetAsync(subscriptionId, cancellationToken: ct), customerId, companyId);
    }

    public async Task PrepareInitialPaymentAsync(string subscriptionId, string customerId, Guid companyId, CancellationToken ct)
    {
        if (!StripeAdminEndpoints.TestActionsEnabled(options)) throw new InvalidOperationException("Stripe test required.");
        var subscription = await subscriptions.GetAsync(subscriptionId, cancellationToken: ct);
        Snapshot(subscription, customerId, companyId);
        if (subscription.Status != "incomplete" || subscription.PaymentSettings?.SaveDefaultPaymentMethod == "on_subscription") return;
        await subscriptions.UpdateAsync(subscriptionId, new SubscriptionUpdateOptions
        { PaymentSettings = new SubscriptionPaymentSettingsOptions { SaveDefaultPaymentMethod = "on_subscription" } },
            new RequestOptions { IdempotencyKey = $"diaglink:subscription:{subscriptionId}:save-payment-method" }, ct);
    }

    private StripeSubscriptionSnapshot Snapshot(Subscription subscription, string customerId, Guid companyId)
    {
        if (subscription.CustomerId != customerId || subscription.Metadata?.GetValueOrDefault("diaglink_company_id") != companyId.ToString()
            || subscription.Items?.Data.Count != 1 || subscription.Items.HasMore)
            throw new InvalidOperationException("Stripe subscription ownership or item count mismatch.");
        var item = subscription.Items.Data[0];
        if (item.Price?.Id != options.PriceId || item.Quantity < 0 || item.CurrentPeriodEnd <= item.CurrentPeriodStart)
            throw new InvalidOperationException("Stripe subscription price, quantity or period mismatch.");
        return new(subscription.Id, customerId, subscription.Status,
            DateTime.SpecifyKind(item.CurrentPeriodStart, DateTimeKind.Utc),
            DateTime.SpecifyKind(item.CurrentPeriodEnd, DateTimeKind.Utc), item.Quantity) { ItemId = item.Id };
    }
}

