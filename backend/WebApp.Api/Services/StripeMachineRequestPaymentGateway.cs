using Stripe;
using Stripe.Checkout;
using Microsoft.Extensions.Logging.Abstractions;

namespace WebApp.Api.Services;

public sealed class StripeMachineRequestPaymentGateway : IMachineRequestPaymentGateway
{
    private readonly StripeBillingOptions settings;
    private readonly SessionService sessions;
    private readonly PaymentIntentService paymentIntents;
    private readonly ILogger<StripeMachineRequestPaymentGateway> logger;

    public StripeMachineRequestPaymentGateway(StripeBillingOptions settings)
        : this(settings, new StripeClient(settings.SecretKey), NullLogger<StripeMachineRequestPaymentGateway>.Instance) { }
    public StripeMachineRequestPaymentGateway(StripeBillingOptions settings,
        ILogger<StripeMachineRequestPaymentGateway> logger)
        : this(settings, new StripeClient(settings.SecretKey), logger) { }
    public StripeMachineRequestPaymentGateway(StripeBillingOptions settings, IStripeClient client)
        : this(settings, client, NullLogger<StripeMachineRequestPaymentGateway>.Instance) { }
    private StripeMachineRequestPaymentGateway(StripeBillingOptions settings, IStripeClient client,
        ILogger<StripeMachineRequestPaymentGateway> logger)
    {
        this.settings = settings;
        this.logger = logger;
        sessions = new SessionService(client);
        paymentIntents = new PaymentIntentService(client);
    }

    public async Task<MachineRequestCheckout> CreateAsync(MachineRequestPayment payment, CancellationToken ct)
    {
        RequireTestConfiguration(requireWebhook: false);
        var metadata = Metadata(payment);
        var session = await sessions.CreateAsync(new SessionCreateOptions
        {
            Mode = "payment",
            ClientReferenceId = payment.PaymentRequestId.ToString(),
            CustomerEmail = payment.Email,
            CustomerCreation = "always",
            SuccessUrl = settings.MachineRequestReturnUrl,
            CancelUrl = settings.MachineRequestReturnUrl,
            PaymentMethodTypes = ["card"],
            Metadata = metadata,
            PaymentIntentData = new() { Metadata = metadata, CaptureMethod = "manual", SetupFutureUsage = "off_session" },
            AllowPromotionCodes = false,
            AutomaticTax = new() { Enabled = false },
            LineItems = [new()
            {
                Quantity = 1,
                PriceData = new()
                {
                    Currency = "eur",
                    UnitAmount = payment.AmountCents,
                    ProductData = new() { Name = "DiagLink — préparation documentaire" }
                }
            }]
        }, new RequestOptions { IdempotencyKey = $"diaglink:machine-request-payment:{payment.PaymentRequestId:N}:checkout" }, ct);
        CheckSession(session, payment, requireFuturePaymentIdentity: false);
        var url = StripeWalletTopUpGateway.SafeUrl(session.Url)
            ?? throw new InvalidOperationException("URL Checkout Stripe invalide.");
        return new(session.Id, url);
    }

    public async Task<MachineRequestCheckout> CreateAdditionalAsync(MachineRequestPayment payment,
        AdditionalMachinePaymentContext context, CancellationToken ct)
    {
        RequireTestConfiguration(requireWebhook: false);
        RequireAppReturnUrl();
        ValidateAdditionalContext(payment, context);
        var metadata = Metadata(payment);
        var session = await sessions.CreateAsync(new SessionCreateOptions
        {
            Mode = "payment",
            ClientReferenceId = payment.PaymentRequestId.ToString(),
            Customer = context.StripeCustomerId,
            SuccessUrl = settings.MachineRequestAppReturnUrl,
            CancelUrl = settings.MachineRequestAppReturnUrl,
            PaymentMethodTypes = ["card"],
            Metadata = metadata,
            PaymentIntentData = new() { Metadata = metadata, CaptureMethod = "manual" },
            AllowPromotionCodes = false,
            AutomaticTax = new() { Enabled = false },
            LineItems = [new()
            {
                Quantity = 1,
                PriceData = new()
                {
                    Currency = "eur",
                    UnitAmount = payment.AmountCents,
                    ProductData = new() { Name = "DiagLink — préparation documentaire" }
                }
            }]
        }, new RequestOptions
        {
            IdempotencyKey = $"diaglink:machine-request-payment:{payment.PaymentRequestId:N}:checkout:additional:v1"
        }, ct);
        CheckSession(session, payment, requireFuturePaymentIdentity: false);
        if (session.CustomerId != context.StripeCustomerId)
            throw new InvalidOperationException("Le Customer Stripe de la Session Checkout est incohérent.");
        var url = StripeWalletTopUpGateway.SafeUrl(session.Url)
            ?? throw new InvalidOperationException("URL Checkout Stripe invalide.");
        return new(session.Id, url);
    }

    public async Task<MachineRequestCheckout> CreateAdditionalDocumentsAsync(MachineRequestPayment payment,
        AdditionalDocumentsContext context, CancellationToken ct)
    {
        RequireTestConfiguration(requireWebhook: false);
        RequireAppReturnUrl();
        ValidateAdditionalDocumentsContext(payment, context);
        var metadata = Metadata(payment);
        var session = await sessions.CreateAsync(new SessionCreateOptions
        {
            Mode = "payment", ClientReferenceId = payment.PaymentRequestId.ToString(),
            Customer = context.StripeCustomerId,
            SuccessUrl = settings.MachineRequestAppReturnUrl, CancelUrl = settings.MachineRequestAppReturnUrl,
            PaymentMethodTypes = ["card"], Metadata = metadata,
            PaymentIntentData = new() { Metadata = metadata, CaptureMethod = "manual" },
            AllowPromotionCodes = false, AutomaticTax = new() { Enabled = false },
            LineItems = [new()
            {
                Quantity = 1,
                PriceData = new()
                {
                    Currency = "eur", UnitAmount = payment.AmountCents,
                    ProductData = new() { Name = "DiagLink — ajout de documentation technique" }
                }
            }]
        }, new RequestOptions
        {
            IdempotencyKey = $"diaglink:machine-request-payment:{payment.PaymentRequestId:N}:checkout:additional-documents:v1"
        }, ct);
        CheckSession(session, payment, requireFuturePaymentIdentity: false);
        if (session.CustomerId != context.StripeCustomerId)
            throw new InvalidOperationException("Le Customer Stripe de la Session Checkout est incohérent.");
        var url = StripeWalletTopUpGateway.SafeUrl(session.Url)
            ?? throw new InvalidOperationException("URL Checkout Stripe invalide.");
        return new(session.Id, url);
    }

    public async Task<MachineRequestPaymentProof> ReadAsync(MachineRequestPayment payment, string sessionId, CancellationToken ct)
    {
        RequireTestConfiguration(requireWebhook: false);
        var session = await sessions.GetAsync(sessionId,
            new SessionGetOptions { Expand = ["payment_intent.latest_charge", "payment_intent.payment_method"] }, cancellationToken: ct);
        CheckSession(session, payment, requireFuturePaymentIdentity: true);
        if (session.Id != payment.StripeSessionId || session.Status != "complete")
            return new("pending", "");
        var intent = session.PaymentIntent;
        if (intent is null) return new("pending", "");
        ValidateIntentIdentity(intent, payment);
        if (MachineRequestPreparationPricing.IncludesMaximumFirstSubscription(payment.TotalPages, payment.AmountCents)
            && session.CustomerId != intent.CustomerId)
            throw new InvalidOperationException("Le Customer Stripe de la Session ne correspond pas au PaymentIntent.");
        return IsAuthorized(intent, payment) ? new("authorized", intent.Id) : new("pending", intent.Id);
    }

    public async Task<MachineRequestPaymentProof> ReadAdditionalAsync(MachineRequestPayment payment, string sessionId,
        AdditionalMachinePaymentContext context, CancellationToken ct)
    {
        RequireTestConfiguration(requireWebhook: false);
        ValidateAdditionalContext(payment, context);
        var session = await sessions.GetAsync(sessionId,
            new SessionGetOptions { Expand = ["payment_intent.latest_charge"] }, cancellationToken: ct);
        CheckSession(session, payment, requireFuturePaymentIdentity: false);
        if (session.CustomerId != context.StripeCustomerId)
            throw new InvalidOperationException("Le Customer Stripe de la Session Checkout est incohérent.");
        if (session.Id != payment.StripeSessionId || session.Status != "complete")
            return new("pending", "");
        var intent = session.PaymentIntent;
        if (intent is null) return new("pending", "");
        ValidateIntentIdentity(intent, payment);
        if (intent.CustomerId != context.StripeCustomerId)
            throw new InvalidOperationException("Le Customer Stripe du PaymentIntent est incohérent.");
        return IsAuthorized(intent, payment) ? new("authorized", intent.Id) : new("pending", intent.Id);
    }

    public async Task<MachineRequestPaymentProof> ReadAdditionalDocumentsAsync(MachineRequestPayment payment,
        string sessionId, AdditionalDocumentsContext context, CancellationToken ct)
    {
        RequireTestConfiguration(requireWebhook: false);
        ValidateAdditionalDocumentsContext(payment, context);
        var session = await sessions.GetAsync(sessionId,
            new SessionGetOptions { Expand = ["payment_intent.latest_charge"] }, cancellationToken: ct);
        CheckSession(session, payment, requireFuturePaymentIdentity: false);
        if (session.CustomerId != context.StripeCustomerId)
            throw new InvalidOperationException("Le Customer Stripe de la Session Checkout est incohérent.");
        if (session.Id != payment.StripeSessionId || session.Status != "complete") return new("pending", "");
        var intent = session.PaymentIntent;
        if (intent is null) return new("pending", "");
        ValidateIntentIdentity(intent, payment);
        if (intent.CustomerId != context.StripeCustomerId)
            throw new InvalidOperationException("Le Customer Stripe du PaymentIntent est incohérent.");
        return IsAuthorized(intent, payment) ? new("authorized", intent.Id) : new("pending", intent.Id);
    }

    public async Task<MachineRequestPaymentProof> AbandonAdditionalDocumentsCheckoutAsync(
        MachineRequestPayment payment, AdditionalDocumentsContext context, CancellationToken ct)
    {
        RequireTestConfiguration(requireWebhook: false);
        ValidateAdditionalDocumentsContext(payment, context);
        if (string.IsNullOrWhiteSpace(payment.StripeSessionId)) return new("abandoned", "");

        var session = await sessions.GetAsync(payment.StripeSessionId,
            new SessionGetOptions { Expand = ["payment_intent.latest_charge"] }, cancellationToken: ct);
        ValidateAbandonSessionIdentity(session, payment, context);
        if (session.Status == "expired") return new("abandoned", "");
        if (session.Status == "open")
        {
            var expired = await sessions.ExpireAsync(session.Id, new SessionExpireOptions(),
                new RequestOptions
                { IdempotencyKey = $"diaglink:machine-request-payment:{payment.PaymentRequestId:N}:expire-checkout:additional-documents:v1" }, ct);
            ValidateAbandonSessionIdentity(expired, payment, context);
            return expired.Status == "expired" ? new("abandoned", "")
                : throw new InvalidOperationException("L'expiration de la Session Checkout n'est pas confirmée.");
        }
        if (session.Status != "complete" || session.PaymentIntent is null)
            throw new InvalidOperationException("La Session Checkout AdditionalDocuments est dans un état incohérent.");

        var intent = session.PaymentIntent;
        ValidateAbandonIntentIdentity(intent, payment, context);
        if (IsAuthorized(intent, payment)) return new("authorized", intent.Id);
        if (IsCancelled(intent, payment)) return new("cancelled", intent.Id);
        if (IsCaptured(intent, payment)) return new("captured", intent.Id);
        throw new InvalidOperationException("Le PaymentIntent AdditionalDocuments n'est pas annulable.");
    }

    public async Task<MachineRequestPaymentProof> CaptureAsync(MachineRequestPayment payment, CancellationToken ct)
        => await CaptureInternalAsync(payment, null,
            $"diaglink:machine-request-payment:{payment.PaymentRequestId:N}:capture:v2", ct);

    public async Task<MachineRequestPaymentProof> CaptureAdditionalAsync(MachineRequestPayment payment,
        AdditionalMachinePaymentContext context, CancellationToken ct)
    {
        ValidateAdditionalContext(payment, context);
        return await CaptureInternalAsync(payment, context.StripeCustomerId,
            $"diaglink:machine-request-payment:{payment.PaymentRequestId:N}:capture:v2", ct);
    }

    public async Task<MachineRequestPaymentProof> CaptureAdditionalDocumentsAsync(MachineRequestPayment payment,
        AdditionalDocumentsContext context, CancellationToken ct)
    {
        ValidateAdditionalDocumentsContext(payment, context);
        return await CaptureInternalAsync(payment, context.StripeCustomerId,
            $"diaglink:machine-request-payment:{payment.PaymentRequestId:N}:capture:additional-documents:v1", ct);
    }

    private async Task<MachineRequestPaymentProof> CaptureInternalAsync(MachineRequestPayment payment,
        string? expectedCustomerId, string idempotencyKey, CancellationToken ct)
    {
        RequireTestConfiguration(requireWebhook: false);
        var amountToCapture = payment.FinalCaptureAmountCents ?? payment.AmountCents;
        logger.LogInformation(
            "Stripe machine request capture stage {CaptureStage} for PaymentRequestId {PaymentRequestId}",
            "PreCaptureRead", payment.PaymentRequestId);
        var before = await GetIntentAsync(payment, ct);
        ValidateCaptureCustomer(before, expectedCustomerId);
        if (IsCaptured(before, payment)) return new("captured", before.Id);
        if (!IsAuthorized(before, payment)) throw new InvalidOperationException("Le paiement Stripe n'est pas capturable.");
        logger.LogInformation(
            "Stripe machine request capture stage {CaptureStage} for PaymentRequestId {PaymentRequestId}",
            "CaptureRequest", payment.PaymentRequestId);
        await paymentIntents.CaptureAsync(before.Id, new PaymentIntentCaptureOptions
            { AmountToCapture = amountToCapture },
            new RequestOptions { IdempotencyKey = idempotencyKey }, ct);
        logger.LogInformation(
            "Stripe machine request capture stage {CaptureStage} for PaymentRequestId {PaymentRequestId}",
            "PostCaptureRead", payment.PaymentRequestId);
        var after = await GetIntentAsync(payment, ct);
        ValidateCaptureCustomer(after, expectedCustomerId);
        return IsCaptured(after, payment) ? new("captured", after.Id)
            : throw new InvalidOperationException("La capture Stripe n'est pas confirmée.");
    }

    private static void ValidateCaptureCustomer(PaymentIntent intent, string? expectedCustomerId)
    {
        if (expectedCustomerId is not null && intent.CustomerId != expectedCustomerId)
            throw new InvalidOperationException("Le Customer Stripe du PaymentIntent est incohérent.");
    }

    public async Task<MachineRequestPaymentProof> CancelAsync(MachineRequestPayment payment, CancellationToken ct)
        => await CancelInternalAsync(payment, null,
            $"diaglink:machine-request-payment:{payment.PaymentRequestId:N}:cancel", ct);

    public async Task<MachineRequestPaymentProof> CancelAdditionalAsync(MachineRequestPayment payment,
        AdditionalMachinePaymentContext context, CancellationToken ct)
    {
        ValidateAdditionalContext(payment, context);
        return await CancelInternalAsync(payment, context.StripeCustomerId,
            $"diaglink:machine-request-payment:{payment.PaymentRequestId:N}:cancel", ct);
    }

    public async Task<MachineRequestPaymentProof> CancelAdditionalDocumentsAsync(MachineRequestPayment payment,
        AdditionalDocumentsContext context, CancellationToken ct)
    {
        ValidateAdditionalDocumentsContext(payment, context);
        return await CancelInternalAsync(payment, context.StripeCustomerId,
            $"diaglink:machine-request-payment:{payment.PaymentRequestId:N}:cancel:additional-documents:v1", ct);
    }

    private async Task<MachineRequestPaymentProof> CancelInternalAsync(MachineRequestPayment payment,
        string? expectedCustomerId, string idempotencyKey, CancellationToken ct)
    {
        RequireTestConfiguration(requireWebhook: false);
        var before = await GetIntentAsync(payment, ct);
        ValidateCaptureCustomer(before, expectedCustomerId);
        if (IsCancelled(before, payment)) return new("cancelled", before.Id);
        if (!IsAuthorized(before, payment)) throw new InvalidOperationException("Le paiement Stripe n'est pas annulable.");
        await paymentIntents.CancelAsync(before.Id, new PaymentIntentCancelOptions { CancellationReason = "requested_by_customer" },
            new RequestOptions { IdempotencyKey = idempotencyKey }, ct);
        var after = await GetIntentAsync(payment, ct);
        ValidateCaptureCustomer(after, expectedCustomerId);
        return IsCancelled(after, payment) ? new("cancelled", after.Id)
            : throw new InvalidOperationException("L'annulation Stripe n'est pas confirmée.");
    }

    private async Task<PaymentIntent> GetIntentAsync(MachineRequestPayment payment, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(payment.StripePaymentIntentId))
            throw new InvalidOperationException("PaymentIntent Stripe absent.");
        var intent = await paymentIntents.GetAsync(payment.StripePaymentIntentId,
            new PaymentIntentGetOptions { Expand = ["latest_charge", "payment_method"] }, cancellationToken: ct);
        ValidateIntentIdentity(intent, payment);
        return intent;
    }

    private void RequireTestConfiguration(bool requireWebhook)
    {
        if (!settings.Enabled || !(settings.SecretKey.StartsWith("sk_test_", StringComparison.Ordinal)
            || settings.SecretKey.StartsWith("rk_test_", StringComparison.Ordinal)))
            throw new InvalidOperationException("Stripe test requis.");
        if (!Uri.TryCreate(settings.MachineRequestReturnUrl, UriKind.Absolute, out var returnUri)
            || returnUri.Scheme is not ("https" or "http"))
            throw new InvalidOperationException("STRIPE_MACHINE_REQUEST_RETURN_URL est requis.");
        if (requireWebhook && !settings.MachineRequestWebhookSecret.StartsWith("whsec_", StringComparison.Ordinal))
            throw new InvalidOperationException("Webhook Stripe non configuré.");
    }

    private void RequireAppReturnUrl()
    {
        if (!Uri.TryCreate(settings.MachineRequestAppReturnUrl, UriKind.Absolute, out var returnUri)
            || returnUri.Scheme is not ("https" or "http"))
            throw new InvalidOperationException("STRIPE_MACHINE_REQUEST_APP_RETURN_URL est requis.");
    }

    private static Dictionary<string, string> Metadata(MachineRequestPayment payment)
    {
        var metadata = new Dictionary<string, string>
        {
            ["diaglink_payment_type"] = "machine_request_preparation",
            ["diaglink_payment_request_id"] = payment.PaymentRequestId.ToString(),
            ["diaglink_estimated_total_pages"] = payment.TotalPages.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["diaglink_server_amount_cents"] = payment.AmountCents.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        if (payment.RequestKind != WebApp.Api.Models.MachineRequestKind.AdditionalDocuments
            && MachineRequestPreparationPricing.IncludesMaximumFirstSubscription(payment.TotalPages, payment.AmountCents))
        {
            metadata["diaglink_authorization_model"] = "preparation_plus_subscription_max_v1";
            metadata["diaglink_maximum_subscription_cents"] =
                MachineRequestPreparationPricing.MaximumFirstSubscriptionCents.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        if (payment.RequestKind == WebApp.Api.Models.MachineRequestKind.AdditionalMachine)
        {
            metadata["diaglink_request_kind"] = "additional_machine";
            metadata["diaglink_company_id"] = payment.CompanyId!.Value.ToString();
            metadata["diaglink_requested_by_user_id"] = payment.RequestedByUserId!.Value.ToString();
            metadata["diaglink_preparation_amount_cents"] =
                MachineRequestPreparationPricing.CalculatePreparationCents(payment.TotalPages)
                    .ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        if (payment.RequestKind == WebApp.Api.Models.MachineRequestKind.AdditionalDocuments)
        {
            if (payment.TotalPages <= 0 || payment.AmountCents <= 0
                || !string.Equals(payment.Currency, "EUR", StringComparison.Ordinal)
                || payment.AmountCents != AdditionalDocumentsPricing.CalculateAmountCents(payment.TotalPages))
                throw new InvalidOperationException("Le montant AdditionalDocuments ne correspond pas au tarif serveur par page.");
            metadata["diaglink_payment_type"] = "machine_request_additional_documents";
            metadata["diaglink_request_kind"] = "additional_documents";
            metadata["diaglink_machine_request_id"] = payment.MachineRequestId!;
            metadata["diaglink_company_id"] = payment.CompanyId!.Value.ToString();
            metadata["diaglink_requested_by_user_id"] = payment.RequestedByUserId!.Value.ToString();
            metadata["diaglink_target_machine_id"] = payment.TargetMachineId!.Value.ToString();
            metadata["diaglink_total_pages"] = payment.TotalPages.ToString(System.Globalization.CultureInfo.InvariantCulture);
            metadata["diaglink_pricing_model"] = "additional_documents_per_page_v1";
        }
        return metadata;
    }

    private static bool MetadataMatches(IDictionary<string, string>? metadata, MachineRequestPayment payment)
    {
        var expected = Metadata(payment);
        return metadata is not null && expected.All(item => metadata.TryGetValue(item.Key, out var value) && value == item.Value);
    }

    private static void ValidateAbandonSessionIdentity(Session session, MachineRequestPayment payment,
        AdditionalDocumentsContext context)
    {
        if (session.Livemode || session.Id != payment.StripeSessionId || session.Mode != "payment"
            || session.ClientReferenceId != payment.PaymentRequestId.ToString()
            || session.CustomerId != context.StripeCustomerId
            || session.Metadata is null
            || !session.Metadata.TryGetValue("diaglink_payment_request_id", out var paymentId)
            || paymentId != payment.PaymentRequestId.ToString()
            || !session.Metadata.TryGetValue("diaglink_request_kind", out var requestKind)
            || requestKind != "additional_documents")
            throw new InvalidOperationException("La Session Checkout AdditionalDocuments est incohérente.");
    }

    private static void ValidateAbandonIntentIdentity(PaymentIntent intent, MachineRequestPayment payment,
        AdditionalDocumentsContext context)
    {
        if (intent.Livemode || intent.CaptureMethod != "manual" || intent.Currency != "eur"
            || intent.Amount != payment.AmountCents || intent.CustomerId != context.StripeCustomerId
            || intent.Metadata is null
            || !intent.Metadata.TryGetValue("diaglink_payment_request_id", out var paymentId)
            || paymentId != payment.PaymentRequestId.ToString()
            || !intent.Metadata.TryGetValue("diaglink_request_kind", out var requestKind)
            || requestKind != "additional_documents")
            throw new InvalidOperationException("Le PaymentIntent AdditionalDocuments est incohérent.");
    }

    private static void ValidateIntentIdentity(PaymentIntent intent, MachineRequestPayment payment)
    {
        if (intent.Livemode || intent.Id != (payment.StripePaymentIntentId ?? intent.Id)
            || intent.CaptureMethod != "manual" || intent.Currency != "eur" || intent.Amount != payment.AmountCents
            || !MetadataMatches(intent.Metadata, payment))
            throw new InvalidOperationException("PaymentIntent Stripe incohérent.");
        if (payment.RequestKind == WebApp.Api.Models.MachineRequestKind.InitialMachine
            && MachineRequestPreparationPricing.IncludesMaximumFirstSubscription(payment.TotalPages, payment.AmountCents)
            && (string.IsNullOrEmpty(intent.CustomerId) || string.IsNullOrEmpty(intent.PaymentMethodId)
                || intent.PaymentMethod?.CustomerId != intent.CustomerId))
            throw new InvalidOperationException("Le Customer ou le moyen de paiement Stripe sauvegardé est incohérent.");
    }

    private static bool IsAuthorized(PaymentIntent intent, MachineRequestPayment payment)
    {
        var charge = intent.LatestCharge;
        return intent.Status == "requires_capture" && intent.AmountCapturable == payment.AmountCents && intent.AmountReceived == 0
            && charge is not null && !charge.Livemode && charge.Paid && !charge.Captured && charge.Status == "succeeded"
            && charge.Amount == payment.AmountCents && charge.Currency == "eur" && charge.AmountRefunded == 0
            && charge.PaymentIntentId == intent.Id;
    }

    private static void ValidateAdditionalContext(MachineRequestPayment payment,
        AdditionalMachinePaymentContext context)
    {
        if (payment.RequestKind != WebApp.Api.Models.MachineRequestKind.AdditionalMachine
            || payment.CompanyId != context.CompanyId || payment.RequestedByUserId != context.UserId
            || string.IsNullOrWhiteSpace(context.StripeCustomerId))
            throw new InvalidOperationException("Le contexte AdditionalMachine est incohérent.");
    }

    private static void ValidateAdditionalDocumentsContext(MachineRequestPayment payment,
        AdditionalDocumentsContext context)
    {
        if (payment.RequestKind != WebApp.Api.Models.MachineRequestKind.AdditionalDocuments
            || payment.CompanyId != context.CompanyId || payment.RequestedByUserId != context.UserId
            || payment.TargetMachineId != context.MachineId || string.IsNullOrWhiteSpace(payment.MachineRequestId)
            || string.IsNullOrWhiteSpace(context.StripeCustomerId))
            throw new InvalidOperationException("Le contexte AdditionalDocuments est incohérent.");
    }

    private static bool IsCaptured(PaymentIntent intent, MachineRequestPayment payment)
    {
        var charge = intent.LatestCharge;
        var capturedAmount = payment.FinalCaptureAmountCents ?? payment.AmountCents;
        return intent.Status == "succeeded" && intent.AmountCapturable == 0 && intent.AmountReceived == capturedAmount
            && charge is not null && !charge.Livemode && charge.Paid && charge.Captured && charge.Status == "succeeded"
            && charge.Amount == payment.AmountCents && charge.Currency == "eur" && charge.AmountRefunded == 0
            && charge.AmountCaptured == capturedAmount
            && charge.PaymentIntentId == intent.Id;
    }

    private static bool IsCancelled(PaymentIntent intent, MachineRequestPayment payment) =>
        intent.Status == "canceled" && intent.AmountCapturable == 0 && intent.AmountReceived == 0;

    private static void CheckSession(Session session, MachineRequestPayment payment, bool requireFuturePaymentIdentity)
    {
        if (session.Livemode || session.Mode != "payment" || session.ClientReferenceId != payment.PaymentRequestId.ToString()
            || session.Currency != "eur" || session.AmountTotal != payment.AmountCents || session.AmountSubtotal != payment.AmountCents
            || !MetadataMatches(session.Metadata, payment))
            throw new InvalidOperationException("Checkout Stripe incohérent.");
        if (requireFuturePaymentIdentity
            && MachineRequestPreparationPricing.IncludesMaximumFirstSubscription(payment.TotalPages, payment.AmountCents)
            && string.IsNullOrEmpty(session.CustomerId))
            throw new InvalidOperationException("Customer Stripe absent de la Session Checkout confirmée.");
    }
}
