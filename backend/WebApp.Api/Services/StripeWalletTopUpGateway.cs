using Stripe;
using Stripe.Checkout;
using WebApp.Api.Models.Entities;
namespace WebApp.Api.Services;

public sealed class StripeWalletTopUpGateway : IStripeWalletTopUpGateway
{
    private readonly StripeBillingOptions settings;
    private readonly SessionService sessions;
    public StripeWalletTopUpGateway(StripeBillingOptions settings) : this(settings, new StripeClient(settings.SecretKey)) { }
    public StripeWalletTopUpGateway(StripeBillingOptions settings, IStripeClient client)
    { this.settings = settings; sessions = new(client); }
    private void RequireTest()
    { if (!StripeAdminEndpoints.TestActionsEnabled(settings)) throw new InvalidOperationException("Stripe test required."); }
    private static Dictionary<string, string> Metadata(StripeWalletTopUp op) => new()
    { ["diaglink_topup_id"] = op.Id.ToString(), ["diaglink_company_id"] = op.CompanyId.ToString() };
    public async Task<StripeTopUpPayment> CreateAsync(StripeWalletTopUp op, CancellationToken ct)
    {
        RequireTest();
        var session = await sessions.CreateAsync(new SessionCreateOptions
        {
            Mode = "payment", Customer = op.StripeCustomerId, ClientReferenceId = op.Id.ToString(),
            SuccessUrl = op.ReturnUrl, CancelUrl = op.ReturnUrl, PaymentMethodTypes = ["card"],
            Metadata = Metadata(op), PaymentIntentData = new() { Metadata = Metadata(op) },
            AllowPromotionCodes = false, AutomaticTax = new() { Enabled = false },
            LineItems = [new() { Quantity = 1, PriceData = new() { Currency = "eur", UnitAmount = op.AmountCents,
                ProductData = new() { Name = "DiagLink — recharge CompanyWallet" } } }]
        }, new RequestOptions { IdempotencyKey = $"diaglink:wallet-topup:{op.Id:N}:checkout" }, ct);
        CheckOwner(session, op);
        return new(session.Id, SafeUrl(session.Url), "AwaitingPayment");
    }
    public static string? SafeUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && uri.Host == "checkout.stripe.com" && string.IsNullOrEmpty(uri.UserInfo) ? value : null;
    private static void CheckOwner(Session session, StripeWalletTopUp op)
    {
        if (session.Livemode || session.Mode != "payment" || session.CustomerId != op.StripeCustomerId
            || session.Metadata?.GetValueOrDefault("diaglink_topup_id") != op.Id.ToString()
            || session.Metadata?.GetValueOrDefault("diaglink_company_id") != op.CompanyId.ToString()
            || session.ClientReferenceId != op.Id.ToString() || session.Currency != "eur"
            || session.AmountTotal != op.AmountCents || session.AmountSubtotal != op.AmountCents)
            throw new InvalidOperationException("Checkout mismatch; reconciliation required.");
    }
    public async Task<StripeTopUpPayment> ReadAsync(StripeWalletTopUp op, string sessionId, CancellationToken ct)
    {
        RequireTest();
        var session = await sessions.GetAsync(sessionId, new SessionGetOptions { Expand = ["payment_intent.latest_charge"] }, cancellationToken: ct);
        CheckOwner(session, op);
        if (session.Id != sessionId || (op.StripeSessionId != null && session.Id != op.StripeSessionId))
            throw new InvalidOperationException("Session mismatch.");
        if (session.Status == "expired") return new(session.Id, null, "ReconciliationRequired");
        if (session.Status != "complete" || session.PaymentStatus != "paid") return new(session.Id, SafeUrl(session.Url), "AwaitingPayment");
        var pi = session.PaymentIntent; var charge = pi?.LatestCharge;
        if (pi == null || pi.Livemode || pi.Status != "succeeded" || pi.Currency != "eur"
            || pi.CustomerId != op.StripeCustomerId || pi.Amount != op.AmountCents || pi.AmountReceived != op.AmountCents
            || pi.Metadata?.GetValueOrDefault("diaglink_topup_id") != op.Id.ToString()
            || pi.Metadata?.GetValueOrDefault("diaglink_company_id") != op.CompanyId.ToString()
            || charge == null || !charge.Paid || !charge.Captured || charge.AmountRefunded != 0
            || charge.PaymentIntentId != pi.Id || charge.CustomerId != op.StripeCustomerId
            || charge.Currency != "eur" || charge.Amount != op.AmountCents || charge.Status != "succeeded")
            return new(session.Id, null, "ReconciliationRequired");
        return new(session.Id, null, "PaymentConfirmed", pi.Id, DateTime.SpecifyKind(charge.Created, DateTimeKind.Utc));
    }
}
