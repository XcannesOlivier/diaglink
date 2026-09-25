using Stripe;
using Stripe.Checkout;
using WebApp.Api.Models;

namespace WebApp.Api.Services;

public sealed record MachineRequestStripeIdentity(string CustomerId, string PaymentMethodId);

public interface IMachineRequestCustomerLinkGateway
{
    Task<MachineRequestStripeIdentity> ReadAndValidateAsync(MachineRequestPayment payment, CancellationToken ct);
    Task<MachineRequestStripeIdentity> ReadAndValidateExistingCustomerAsync(MachineRequestPayment payment,
        string expectedCustomerId, CancellationToken ct) => ReadAndValidateAsync(payment, ct);
    Task ConfirmCustomerConfigurationAsync(MachineRequestPayment payment, Guid companyId,
        MachineRequestStripeIdentity identity, CancellationToken ct);
}

public sealed class StripeMachineRequestCustomerLinkGateway : IMachineRequestCustomerLinkGateway
{
    private const string CompanyMetadata = "diaglink_company_id";
    private const string PaymentMetadata = "diaglink_machine_request_payment_id";
    private readonly StripeBillingOptions settings;
    private readonly SessionService sessions;
    private readonly PaymentIntentService intents;
    private readonly PaymentMethodService methods;
    private readonly CustomerService customers;

    public StripeMachineRequestCustomerLinkGateway(StripeBillingOptions settings) : this(settings, new StripeClient(settings.SecretKey)) { }
    public StripeMachineRequestCustomerLinkGateway(StripeBillingOptions settings, IStripeClient client)
    {
        this.settings = settings; sessions = new(client); intents = new(client); methods = new(client); customers = new(client);
    }

    public async Task<MachineRequestStripeIdentity> ReadAndValidateAsync(MachineRequestPayment payment, CancellationToken ct)
        => (await ReadAndValidateCoreAsync(payment, requireAttachedPaymentMethod: true, ct)).Identity;

    private async Task<(MachineRequestStripeIdentity Identity, Customer Customer)> ReadAndValidateCoreAsync(
        MachineRequestPayment payment, bool requireAttachedPaymentMethod, CancellationToken ct)
    {
        RequireTest();
        if (string.IsNullOrWhiteSpace(payment.StripeSessionId) || string.IsNullOrWhiteSpace(payment.StripePaymentIntentId))
            throw new InvalidOperationException("Les références Stripe du paiement sont incomplètes.");
        var session = await sessions.GetAsync(payment.StripeSessionId, cancellationToken: ct);
        var intent = await intents.GetAsync(payment.StripePaymentIntentId, cancellationToken: ct);
        if (session.Livemode || session.Id != payment.StripeSessionId || session.PaymentIntentId != intent.Id
            || session.ClientReferenceId != payment.PaymentRequestId.ToString() || session.Mode != "payment"
            || session.Currency != "eur" || session.AmountTotal != payment.AmountCents
            || !PaymentMetadataMatches(session.Metadata, payment))
            throw new InvalidOperationException("La Session Checkout Stripe est incohérente.");
        if (intent.Livemode || intent.Id != payment.StripePaymentIntentId || intent.Status != "succeeded"
            || intent.Amount != payment.AmountCents || intent.AmountReceived != payment.FinalCaptureAmountCents
            || intent.AmountCapturable != 0 || intent.CaptureMethod != "manual" || intent.Currency != "eur"
            || !PaymentMetadataMatches(intent.Metadata, payment))
            throw new InvalidOperationException("Le PaymentIntent Stripe capturé est incohérent.");
        if (string.IsNullOrWhiteSpace(session.CustomerId) || string.IsNullOrWhiteSpace(intent.CustomerId)
            || session.CustomerId != intent.CustomerId)
            throw new InvalidOperationException("Les Customers Stripe de la Session et du PaymentIntent ne correspondent pas.");
        if (string.IsNullOrWhiteSpace(intent.PaymentMethodId)) throw new InvalidOperationException("PaymentMethod Stripe absent.");
        var method = await methods.GetAsync(intent.PaymentMethodId, cancellationToken: ct);
        if (method.Livemode || (requireAttachedPaymentMethod && method.CustomerId != intent.CustomerId))
            throw new InvalidOperationException("Le PaymentMethod Stripe n'appartient pas au Customer attendu.");
        var customer = await customers.GetAsync(intent.CustomerId, cancellationToken: ct);
        if (customer.Deleted == true || customer.Livemode)
            throw new InvalidOperationException("Customer Stripe introuvable ou invalide.");
        return (new(customer.Id, method.Id), customer);
    }

    public async Task<MachineRequestStripeIdentity> ReadAndValidateExistingCustomerAsync(
        MachineRequestPayment payment, string expectedCustomerId, CancellationToken ct)
    {
        var validated = await ReadAndValidateCoreAsync(payment, requireAttachedPaymentMethod: false, ct);
        var identity = validated.Identity;
        if (!string.Equals(identity.CustomerId, expectedCustomerId, StringComparison.Ordinal))
            throw new InvalidOperationException("Le PaymentIntent n'appartient pas au Customer Stripe de l'entreprise.");
        if (validated.Customer.Metadata?.TryGetValue(CompanyMetadata, out var companyId) == true
            && companyId != payment.CompanyId?.ToString())
            throw new InvalidOperationException("Le Customer Stripe n'appartient pas à l'entreprise attendue.");
        return identity;
    }

    public async Task ConfirmCustomerConfigurationAsync(MachineRequestPayment payment, Guid companyId,
        MachineRequestStripeIdentity identity, CancellationToken ct)
    {
        var customer = await customers.GetAsync(identity.CustomerId, cancellationToken: ct);
        var expectedCompany = companyId.ToString();
        var expectedPayment = payment.PaymentRequestId.ToString();
        RejectMetadataConflict(customer.Metadata, CompanyMetadata, expectedCompany);
        RejectMetadataConflict(customer.Metadata, PaymentMetadata, expectedPayment);
        var currentDefault = customer.InvoiceSettings?.DefaultPaymentMethodId;
        if (!string.IsNullOrEmpty(currentDefault) && currentDefault != identity.PaymentMethodId)
            throw new InvalidOperationException("Un autre moyen de paiement Stripe est déjà défini par défaut.");
        var metadata = customer.Metadata is null ? new Dictionary<string, string>() : new(customer.Metadata);
        metadata[CompanyMetadata] = expectedCompany; metadata[PaymentMetadata] = expectedPayment;
        if (currentDefault != identity.PaymentMethodId || !MetadataMatches(customer.Metadata, expectedCompany, expectedPayment))
            await customers.UpdateAsync(identity.CustomerId, new CustomerUpdateOptions
            {
                Metadata = metadata,
                InvoiceSettings = new CustomerInvoiceSettingsOptions { DefaultPaymentMethod = identity.PaymentMethodId }
            }, new RequestOptions { IdempotencyKey = $"diaglink:machine-request-payment:{payment.PaymentRequestId:N}:link-customer" }, ct);
        customer = await customers.GetAsync(identity.CustomerId, cancellationToken: ct);
        if (customer.Deleted == true || customer.InvoiceSettings?.DefaultPaymentMethodId != identity.PaymentMethodId
            || !MetadataMatches(customer.Metadata, expectedCompany, expectedPayment))
            throw new InvalidOperationException("La configuration du Customer Stripe n'est pas confirmée.");
    }

    private void RequireTest()
    {
        if (!settings.Enabled || !(settings.SecretKey.StartsWith("sk_test_", StringComparison.Ordinal)
            || settings.SecretKey.StartsWith("rk_test_", StringComparison.Ordinal)))
            throw new InvalidOperationException("Stripe test requis.");
    }
    private static void RejectMetadataConflict(IDictionary<string, string>? values, string key, string expected)
    { if (values?.TryGetValue(key, out var value) == true && value != expected) throw new InvalidOperationException("Les metadata du Customer Stripe sont en conflit."); }
    private static bool MetadataMatches(IDictionary<string, string>? values, string company, string payment) =>
        values is not null && values.TryGetValue(CompanyMetadata, out var storedCompany) && storedCompany == company
        && values.TryGetValue(PaymentMetadata, out var storedPayment) && storedPayment == payment;
    private static bool PaymentMetadataMatches(IDictionary<string, string>? values, MachineRequestPayment payment) =>
        values is not null
        && values.TryGetValue("diaglink_payment_type", out var type) && type == "machine_request_preparation"
        && values.TryGetValue("diaglink_payment_request_id", out var id) && id == payment.PaymentRequestId.ToString()
        && values.TryGetValue("diaglink_server_amount_cents", out var amount)
        && amount == payment.AmountCents.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public sealed class MachineRequestCustomerLinkService(MachineRequestPaymentStore store,
    StripeBillingService billing, IMachineRequestCustomerLinkGateway gateway,
    AdditionalMachinePaymentContextResolver additionalContextResolver)
{
    public async Task<MachineRequestPayment?> LinkAsync(Guid paymentRequestId, CancellationToken ct)
    {
        var payment = await store.GetAsync(paymentRequestId, ct);
        if (payment is null) return null;
        if (payment.Status != "captured" || payment.CompanyId is null || payment.MachineId is null
            || payment.ProvisioningStage < Models.Entities.MachineRequestProvisioningStage.BusinessEntitiesCreated)
            throw new InvalidOperationException("Le paiement capturé et le rattachement entreprise/machine sont requis.");
        if (payment.RequestKind == MachineRequestKind.AdditionalMachine)
        {
            var resolution = await additionalContextResolver.ResolveStoredPaymentAsync(payment, ct);
            var context = resolution.Context ?? throw new InvalidOperationException(resolution.ErrorMessage);
            var existingIdentity = await gateway.ReadAndValidateExistingCustomerAsync(
                payment, context.StripeCustomerId, ct);
            return await store.MarkCustomerLinkedAsync(payment, existingIdentity.CustomerId, ct);
        }
        var identity = await gateway.ReadAndValidateAsync(payment, ct);
        await billing.LinkExistingCustomerAsync(payment.CompanyId.Value, identity.CustomerId, ct);
        await gateway.ConfirmCustomerConfigurationAsync(payment, payment.CompanyId.Value, identity, ct);
        return await store.MarkCustomerLinkedAsync(payment, identity.CustomerId, ct);
    }
}
