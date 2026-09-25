using MachineRequestProvisioningStage = WebApp.Api.Models.Entities.MachineRequestProvisioningStage;
using MachineRequestPreparationStatus = WebApp.Api.Models.Entities.MachineRequestPreparationStatus;
using WebApp.Api.Models;

namespace WebApp.Api.Services;

public sealed record MachineRequestPaymentResult(Guid PaymentRequestId, string Status, decimal Amount, string Currency,
    string? CheckoutUrl = null, decimal? InitialAuthorizationAmount = null,
    string ProvisioningStage = "awaitingAcceptance", Guid? CompanyId = null, Guid? MachineId = null,
    DateTime? ActivatedAtUtc = null, DateTime? FirstPeriodEndUtc = null, int? ServiceAmountCents = null,
    string PreparationStatus = "pending", DateTime? ReadyAtUtc = null, Guid? ReadyByUserId = null);
public sealed record MachineRequestPaymentVerification(string Status, MachineRequestPayment? Payment);

public sealed class MachineRequestPaymentService
{
    private readonly MachineRequestPaymentStore store;
    private readonly IMachineRequestPaymentGateway gateway;
    private readonly TimeProvider timeProvider;
    private readonly AdditionalMachinePaymentContextResolver? additionalContextResolver;
    private readonly AdditionalDocumentsContextResolver? additionalDocumentsContextResolver;
    private readonly RequestReceivedNotificationService? requestReceivedNotifications;

    public MachineRequestPaymentService(MachineRequestPaymentStore store, IMachineRequestPaymentGateway gateway)
        : this(store, gateway, null, TimeProvider.System) { }

    public MachineRequestPaymentService(MachineRequestPaymentStore store, IMachineRequestPaymentGateway gateway,
        TimeProvider timeProvider)
        : this(store, gateway, null, timeProvider) { }

    public MachineRequestPaymentService(MachineRequestPaymentStore store, IMachineRequestPaymentGateway gateway,
        AdditionalMachinePaymentContextResolver? additionalContextResolver, TimeProvider timeProvider)
        : this(store, gateway, additionalContextResolver, null, timeProvider) { }

    public MachineRequestPaymentService(MachineRequestPaymentStore store, IMachineRequestPaymentGateway gateway,
        AdditionalMachinePaymentContextResolver? additionalContextResolver,
        AdditionalDocumentsContextResolver? additionalDocumentsContextResolver, TimeProvider timeProvider)
        : this(store, gateway, additionalContextResolver, additionalDocumentsContextResolver, timeProvider, null) { }

    public MachineRequestPaymentService(MachineRequestPaymentStore store, IMachineRequestPaymentGateway gateway,
        AdditionalMachinePaymentContextResolver? additionalContextResolver,
        AdditionalDocumentsContextResolver? additionalDocumentsContextResolver, TimeProvider timeProvider,
        RequestReceivedNotificationService? requestReceivedNotifications = null)
    {
        this.store = store;
        this.gateway = gateway;
        this.additionalContextResolver = additionalContextResolver;
        this.additionalDocumentsContextResolver = additionalDocumentsContextResolver;
        this.timeProvider = timeProvider;
        this.requestReceivedNotifications = requestReceivedNotifications;
    }

    public async Task<MachineRequestPaymentResult> CreateAsync(int totalPages, string? email, CancellationToken ct)
    {
        var amountCents = MachineRequestPreparationPricing.CalculateMaximumAuthorizationCents(totalPages);
        var now = DateTime.UtcNow;
        var payment = new MachineRequestPayment(Guid.NewGuid(), totalPages,
            amountCents, "EUR", NormalizeEmail(email), null, null, "pending", now, now,
            RequestKind: MachineRequestKind.InitialMachine,
            RequestedByUserId: null);
        payment = await store.AddAsync(payment, ct);
        try
        {
            var checkout = await gateway.CreateAsync(payment, ct);
            payment = await store.SetCheckoutSessionAsync(payment, checkout.SessionId, ct);
            return Result(payment, checkout.CheckoutUrl);
        }
        catch
        {
            await store.RemovePendingAsync(payment, CancellationToken.None);
            throw;
        }
    }

    public async Task<MachineRequestPaymentResult?> ReadAsync(Guid id, CancellationToken ct)
    {
        var payment = await store.GetAsync(id, ct);
        return payment is null ? null : Result(payment);
    }

    public async Task<MachineRequestPaymentResult> CreateAdditionalAsync(Guid paymentRequestId, int totalPages,
        AdditionalMachinePaymentContext context, CancellationToken ct)
    {
        var amountCents = MachineRequestPreparationPricing.CalculateMaximumAuthorizationCents(totalPages);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var expected = new MachineRequestPayment(paymentRequestId, totalPages, amountCents, "EUR", null,
            null, null, "pending", now, now, CompanyId: context.CompanyId,
            RequestKind: MachineRequestKind.AdditionalMachine, RequestedByUserId: context.UserId);
        var payment = await store.AddOrGetAsync(expected, ct);
        EnsureAdditionalOwnership(payment, context, totalPages, amountCents);
        if (payment.Status != "pending") throw new InvalidOperationException("Cette autorisation a déjà été traitée.");

        var checkout = await gateway.CreateAdditionalAsync(payment, context, ct);
        if (payment.StripeSessionId is null)
        {
            try { payment = await store.SetCheckoutSessionAsync(payment, checkout.SessionId, ct); }
            catch (InvalidOperationException)
            {
                payment = await store.GetAsync(payment.PaymentRequestId, ct)
                    ?? throw new InvalidOperationException("Le paiement est devenu indisponible.");
            }
        }
        if (payment.StripeSessionId != checkout.SessionId)
            throw new InvalidOperationException("La Session Checkout ne correspond pas au paiement existant.");
        return Result(payment, checkout.CheckoutUrl);
    }

    public async Task<MachineRequestPaymentResult?> ReadAdditionalAsync(Guid id,
        AdditionalMachinePaymentContext context, CancellationToken ct)
    {
        var payment = await store.GetAsync(id, ct);
        if (payment is null) return null;
        EnsureAdditionalOwnership(payment, context, payment.TotalPages, payment.AmountCents);
        return Result(payment);
    }

    public async Task<MachineRequestPaymentResult> StartAdditionalDocumentsAsync(Guid id,
        AdditionalDocumentsContext context, CancellationToken ct)
    {
        var payment = await store.GetAsync(id, ct)
            ?? throw new KeyNotFoundException("La tentative documentaire est introuvable.");
        EnsureAdditionalDocumentsOwnership(payment, context);
        if (payment.Status != "pending") throw new InvalidOperationException("Cette autorisation a déjà été traitée.");
        if (payment.AmountCents <= 0 || payment.Currency != "EUR")
            throw new InvalidOperationException("Le montant documentaire durable est incohérent.");
        var checkout = await gateway.CreateAdditionalDocumentsAsync(payment, context, ct);
        if (payment.StripeSessionId is null)
        {
            try { payment = await store.SetCheckoutSessionAsync(payment, checkout.SessionId, ct); }
            catch (InvalidOperationException)
            {
                payment = await store.GetAsync(id, ct)
                    ?? throw new InvalidOperationException("Le paiement est devenu indisponible.");
            }
        }
        if (payment.StripeSessionId != checkout.SessionId)
            throw new InvalidOperationException("La Session Checkout ne correspond pas au paiement existant.");
        return Result(payment, checkout.CheckoutUrl);
    }

    public async Task<MachineRequestPaymentResult?> ReadAdditionalDocumentsAsync(Guid id,
        AdditionalDocumentsContext context, CancellationToken ct)
    {
        var payment = await store.GetAsync(id, ct);
        if (payment is null) return null;
        EnsureAdditionalDocumentsOwnership(payment, context);
        if (payment.Status != "pending" || string.IsNullOrEmpty(payment.StripeSessionId)) return Result(payment);
        var proof = await gateway.ReadAdditionalDocumentsAsync(payment, payment.StripeSessionId, context, ct);
        if (proof.Status == "authorized")
        {
            var status = await store.AuthorizeAsync(payment, proof.PaymentIntentId,
                $"reconcile:{payment.StripeSessionId}", ct);
            if (status == "authorized")
            {
                payment = await store.GetAsync(id, ct)
                    ?? throw new InvalidOperationException("Le paiement est devenu indisponible.");
                if (requestReceivedNotifications is not null)
                    await requestReceivedNotifications.TryEnqueueAsync(id, CancellationToken.None);
            }
        }
        return Result(payment);
    }

    public Task<MachineRequestPayment?> ReadPaymentAsync(Guid id, CancellationToken ct) => store.GetAsync(id, ct);

    public async Task<MachineRequestPaymentResult?> AttachBusinessEntitiesAsync(Guid id, Guid companyId,
        Guid machineId, CancellationToken ct)
    {
        var payment = await store.GetAsync(id, ct);
        if (payment is null) return null;
        payment = await store.AttachBusinessEntitiesAsync(payment, companyId, machineId, ct);
        return Result(payment);
    }

    public async Task<string> ConfirmAuthorizationAsync(Guid id, string sessionId, string eventId, CancellationToken ct)
    {
        var payment = await store.GetAsync(id, ct);
        if (payment is null) return "unknown";
        if (payment.Status is "authorized" or "captured" or "cancelled" or "abandoned")
        {
            if (payment.Status == "authorized" && requestReceivedNotifications is not null)
                await requestReceivedNotifications.TryEnqueueAsync(id, CancellationToken.None);
            return payment.Status;
        }
        if (payment.StripeSessionId != sessionId) return "invalid";
        MachineRequestPaymentProof proof;
        if (payment.RequestKind == MachineRequestKind.AdditionalMachine)
        {
            var resolver = additionalContextResolver
                ?? throw new InvalidOperationException("Le contexte AdditionalMachine n'est pas configuré.");
            var resolution = await resolver.ResolveStoredPaymentAsync(payment, ct);
            if (!resolution.Success || resolution.Context is null)
                throw new InvalidOperationException("Le contexte AdditionalMachine n'est plus valide.");
            proof = await gateway.ReadAdditionalAsync(payment, sessionId, resolution.Context, ct);
        }
        else if (payment.RequestKind == MachineRequestKind.AdditionalDocuments)
        {
            var resolver = additionalDocumentsContextResolver
                ?? throw new InvalidOperationException("Le contexte AdditionalDocuments n'est pas configuré.");
            var resolution = await resolver.ResolveStoredPaymentAsync(payment, ct);
            if (!resolution.Success || resolution.Context is null)
                throw new InvalidOperationException("Le contexte AdditionalDocuments n'est plus valide.");
            proof = await gateway.ReadAdditionalDocumentsAsync(payment, sessionId, resolution.Context, ct);
        }
        else
        {
            proof = await gateway.ReadAsync(payment, sessionId, ct);
        }
        if (proof.Status != "authorized") return "pending";
        var status = await store.AuthorizeAsync(payment, proof.PaymentIntentId, eventId, ct);
        if (status == "authorized" && requestReceivedNotifications is not null)
            await requestReceivedNotifications.TryEnqueueAsync(id, CancellationToken.None);
        return status;
    }

    public async Task<MachineRequestPaymentResult?> CaptureAsync(Guid id, long? preparationAmountCents,
        CancellationToken ct)
    {
        var payment = await store.GetAsync(id, ct);
        if (payment is null) return null;
        if (payment.RequestKind == MachineRequestKind.AdditionalDocuments)
            throw new InvalidOperationException("La capture AdditionalDocuments n'est pas disponible à cette étape.");
        if (payment.Status == "captured") return Result(payment);
        if (payment.Status != "authorized")
            throw new InvalidOperationException("Le paiement n'est pas dans l'état authorized.");

        if (MachineRequestPreparationPricing.IncludesMaximumFirstSubscription(payment.TotalPages, payment.AmountCents))
        {
            if (preparationAmountCents is null)
                throw new InvalidOperationException("Le coût documentaire serveur est requis avant la capture.");
            payment = await store.FinalizeAmountAsync(payment, preparationAmountCents.Value,
                timeProvider.GetUtcNow().UtcDateTime, ct);
        }

        MachineRequestPaymentProof proof;
        if (payment.RequestKind == MachineRequestKind.AdditionalMachine)
        {
            var resolver = additionalContextResolver
                ?? throw new InvalidOperationException("Le contexte AdditionalMachine n'est pas configuré.");
            var resolution = await resolver.ResolveStoredPaymentAsync(payment, ct);
            if (!resolution.Success || resolution.Context is null)
                throw new InvalidOperationException("Le contexte AdditionalMachine n'est plus valide.");
            proof = await gateway.CaptureAdditionalAsync(payment, resolution.Context, ct);
        }
        else proof = await gateway.CaptureAsync(payment, ct);
        if (proof.Status != "captured" || proof.PaymentIntentId != payment.StripePaymentIntentId)
            throw new InvalidOperationException("L'opération Stripe n'a pas été confirmée.");
        payment = await store.CompleteAsync(payment, "captured", ct);
        return Result(payment);
    }

    public Task<MachineRequestPaymentResult?> CaptureAsync(Guid id, CancellationToken ct) =>
        CaptureAsync(id, null, ct);

    public async Task<MachineRequestPaymentResult?> CaptureFromAdminDecisionAsync(Guid id,
        long? preparationAmountCents, MachineRequestRecord request, CancellationToken ct)
    {
        var payment = await store.GetAsync(id, ct);
        if (payment is null) return null;
        if (payment.RequestKind == MachineRequestKind.AdditionalDocuments)
            throw new InvalidOperationException("Utilisez la décision documentaire dédiée.");
        if (payment.Status == "captured")
        {
            payment = await store.CompleteAdminDecisionAsync(payment, "captured",
                MachineRequestDecisionNotifications.Accepted(request, payment), ct);
            return Result(payment);
        }
        if (payment.Status != "authorized")
            throw new InvalidOperationException("Le paiement n'est pas dans l'état authorized.");
        if (MachineRequestPreparationPricing.IncludesMaximumFirstSubscription(payment.TotalPages, payment.AmountCents))
        {
            if (preparationAmountCents is null)
                throw new InvalidOperationException("Le coût documentaire serveur est requis avant la capture.");
            payment = await store.FinalizeAmountAsync(payment, preparationAmountCents.Value,
                timeProvider.GetUtcNow().UtcDateTime, ct);
        }
        MachineRequestPaymentProof proof;
        if (payment.RequestKind == MachineRequestKind.AdditionalMachine)
        {
            var resolver = additionalContextResolver
                ?? throw new InvalidOperationException("Le contexte AdditionalMachine n'est pas configuré.");
            var resolution = await resolver.ResolveStoredPaymentAsync(payment, ct);
            if (!resolution.Success || resolution.Context is null)
                throw new InvalidOperationException("Le contexte AdditionalMachine n'est plus valide.");
            proof = await gateway.CaptureAdditionalAsync(payment, resolution.Context, ct);
        }
        else proof = await gateway.CaptureAsync(payment, ct);
        if (proof.Status != "captured" || proof.PaymentIntentId != payment.StripePaymentIntentId)
            throw new InvalidOperationException("L'opération Stripe n'a pas été confirmée.");
        payment = await store.CompleteAdminDecisionAsync(payment, "captured",
            MachineRequestDecisionNotifications.Accepted(request, payment), ct);
        return Result(payment);
    }

    public Task<MachineRequestPaymentResult?> CancelAsync(Guid id, CancellationToken ct) =>
        ChangeAuthorizationAsync(id, "cancelled", gateway.CancelAsync, ct);

    public async Task<MachineRequestPaymentResult?> CancelFromAdminDecisionAsync(Guid id,
        MachineRequestRecord request, CancellationToken ct)
    {
        var payment = await store.GetAsync(id, ct);
        if (payment is null) return null;
        if (payment.RequestKind == MachineRequestKind.AdditionalDocuments)
            throw new InvalidOperationException("Utilisez la décision documentaire dédiée.");
        if (payment.Status == "cancelled")
        {
            payment = await store.CompleteAdminDecisionAsync(payment, "cancelled",
                MachineRequestDecisionNotifications.Rejected(request, payment), ct);
            return Result(payment);
        }
        if (payment.Status != "authorized" || string.IsNullOrEmpty(payment.StripePaymentIntentId))
            throw new InvalidOperationException("Le paiement n'est pas dans l'état authorized.");
        MachineRequestPaymentProof proof;
        if (payment.RequestKind == MachineRequestKind.AdditionalMachine)
        {
            var resolver = additionalContextResolver
                ?? throw new InvalidOperationException("Le contexte AdditionalMachine n'est pas configuré.");
            var resolution = await resolver.ResolveStoredPaymentAsync(payment, ct);
            if (!resolution.Success || resolution.Context is null)
                throw new InvalidOperationException("Le contexte AdditionalMachine n'est plus valide.");
            proof = await gateway.CancelAdditionalAsync(payment, resolution.Context, ct);
        }
        else proof = await gateway.CancelAsync(payment, ct);
        if (proof.Status != "cancelled" || proof.PaymentIntentId != payment.StripePaymentIntentId)
            throw new InvalidOperationException("L'opération Stripe n'a pas été confirmée.");
        payment = await store.CompleteAdminDecisionAsync(payment, "cancelled",
            MachineRequestDecisionNotifications.Rejected(request, payment), ct);
        return Result(payment);
    }

    public Task<MachineRequestPaymentResult?> CaptureAdditionalDocumentsAsync(Guid id,
        AdditionalDocumentsContext context, CancellationToken ct) =>
        CompleteAdditionalDocumentsAsync(id, "captured", context,
            gateway.CaptureAdditionalDocumentsAsync, ct);

    public Task<MachineRequestPaymentResult?> CancelAdditionalDocumentsAsync(Guid id,
        AdditionalDocumentsContext context, CancellationToken ct) =>
        CompleteAdditionalDocumentsAsync(id, "cancelled", context,
            gateway.CancelAdditionalDocumentsAsync, ct);

    public async Task<MachineRequestPaymentResult?> CompleteAdditionalDocumentsFromAdminDecisionAsync(Guid id,
        bool accept, AdditionalDocumentsContext context, MachineRequestRecord request, CancellationToken ct)
    {
        var payment = await store.GetAsync(id, ct);
        if (payment is null) return null;
        EnsureAdditionalDocumentsOwnership(payment, context);
        var targetStatus = accept ? "captured" : "cancelled";
        if (payment.Status == targetStatus)
        {
            var notification = accept
                ? MachineRequestDecisionNotifications.Accepted(request, payment)
                : MachineRequestDecisionNotifications.Rejected(request, payment);
            payment = await store.CompleteAdminDecisionAsync(payment, targetStatus, notification, ct);
            return Result(payment);
        }
        if (payment.Status != "authorized" || string.IsNullOrWhiteSpace(payment.StripePaymentIntentId))
            throw new InvalidOperationException("La transition de paiement AdditionalDocuments est interdite.");
        var proof = accept
            ? await gateway.CaptureAdditionalDocumentsAsync(payment, context, ct)
            : await gateway.CancelAdditionalDocumentsAsync(payment, context, ct);
        if (proof.Status != targetStatus || proof.PaymentIntentId != payment.StripePaymentIntentId)
            throw new InvalidOperationException("L'opération Stripe AdditionalDocuments n'est pas confirmée.");
        var outbox = accept
            ? MachineRequestDecisionNotifications.Accepted(request, payment)
            : MachineRequestDecisionNotifications.Rejected(request, payment);
        payment = await store.CompleteAdminDecisionAsync(payment, targetStatus, outbox, ct);
        return Result(payment);
    }

    public async Task<MachineRequestPaymentResult?> AbandonAdditionalDocumentsAsync(Guid id,
        AdditionalDocumentsContext context, CancellationToken ct)
    {
        var payment = await store.GetAsync(id, ct);
        if (payment is null) return null;
        EnsureAdditionalDocumentsOwnership(payment, context);
        if (payment.Status is "cancelled" or "abandoned") return Result(payment);
        if (payment.Status == "captured")
            throw new InvalidOperationException("Une demande documentaire encaissée ne peut plus être annulée.");
        if (payment.Status == "authorized")
            return await CancelAdditionalDocumentsAsync(id, context, ct);
        if (payment.Status != "pending")
            throw new InvalidOperationException("La demande documentaire ne peut plus être annulée.");

        if (!string.IsNullOrWhiteSpace(payment.StripeSessionId))
        {
            var proof = await gateway.AbandonAdditionalDocumentsCheckoutAsync(payment, context, ct);
            if (proof.Status == "authorized")
            {
                var status = await store.AuthorizeAsync(payment, proof.PaymentIntentId,
                    $"abandon:{payment.PaymentRequestId:N}", ct);
                if (status != "authorized")
                    throw new InvalidOperationException("L'autorisation Stripe n'a pas pu être réconciliée avant annulation.");
                return await CancelAdditionalDocumentsAsync(id, context, ct);
            }
            if (proof.Status != "abandoned")
                throw new InvalidOperationException("La Session Checkout n'a pas pu être abandonnée.");
        }

        payment = await store.AbandonPendingAsync(payment, ct);
        return Result(payment);
    }

    private async Task<MachineRequestPaymentResult?> CompleteAdditionalDocumentsAsync(Guid id, string targetStatus,
        AdditionalDocumentsContext context,
        Func<MachineRequestPayment, AdditionalDocumentsContext, CancellationToken, Task<MachineRequestPaymentProof>> action,
        CancellationToken ct)
    {
        var payment = await store.GetAsync(id, ct);
        if (payment is null) return null;
        EnsureAdditionalDocumentsOwnership(payment, context);
        if (payment.Status == targetStatus) return Result(payment);
        if (payment.Status != "authorized")
            throw new InvalidOperationException("La transition de paiement AdditionalDocuments est interdite.");
        if (string.IsNullOrWhiteSpace(payment.StripePaymentIntentId))
            throw new InvalidOperationException("La preuve d'autorisation Stripe est absente.");
        var proof = await action(payment, context, ct);
        if (proof.Status != targetStatus || proof.PaymentIntentId != payment.StripePaymentIntentId)
            throw new InvalidOperationException("L'opération Stripe AdditionalDocuments n'est pas confirmée.");
        payment = await store.CompleteAsync(payment, targetStatus, ct);
        return Result(payment);
    }

    public async Task<MachineRequestPaymentVerification> VerifyForMachineRequestAsync(Guid id, CancellationToken ct)
    {
        var payment = await store.GetAsync(id, ct);
        if (payment is null) return new("not_found", null);
        _ = MachineRequestPreparationPricing.IncludesMaximumFirstSubscription(payment.TotalPages, payment.AmountCents);
        if (payment.Status != "authorized" || string.IsNullOrEmpty(payment.StripeSessionId)
            || string.IsNullOrEmpty(payment.StripePaymentIntentId))
            return new(payment.Status, payment);
        var proof = await gateway.ReadAsync(payment, payment.StripeSessionId, ct);
        return proof.Status == "authorized" && proof.PaymentIntentId == payment.StripePaymentIntentId
            ? new("authorized", payment)
            : new("authorization_invalid", payment);
    }

    public async Task<MachineRequestPaymentVerification> VerifyAdditionalForMachineRequestAsync(
        Guid id, AdditionalMachinePaymentContext context, CancellationToken ct)
    {
        var payment = await store.GetAsync(id, ct);
        if (payment is null) return new("not_found", null);
        EnsureAdditionalOwnership(payment, context, payment.TotalPages, payment.AmountCents);
        _ = MachineRequestPreparationPricing.IncludesMaximumFirstSubscription(payment.TotalPages, payment.AmountCents);
        if (payment.Status != "authorized" || string.IsNullOrEmpty(payment.StripeSessionId)
            || string.IsNullOrEmpty(payment.StripePaymentIntentId))
            return new(payment.Status, payment);
        var proof = await gateway.ReadAdditionalAsync(payment, payment.StripeSessionId, context, ct);
        return proof.Status == "authorized" && proof.PaymentIntentId == payment.StripePaymentIntentId
            ? new("authorized", payment)
            : new("authorization_invalid", payment);
    }

    public Task<MachineRequestPayment> ReserveMachineRequestAsync(MachineRequestPayment payment, CancellationToken ct) =>
        store.ReserveMachineRequestAsync(payment, ct);

    private async Task<MachineRequestPaymentResult?> ChangeAuthorizationAsync(Guid id, string expectedStatus,
        Func<MachineRequestPayment, CancellationToken, Task<MachineRequestPaymentProof>> action, CancellationToken ct)
    {
        var payment = await store.GetAsync(id, ct);
        if (payment is null) return null;
        if (payment.RequestKind == MachineRequestKind.AdditionalDocuments)
            throw new InvalidOperationException("L'annulation AdditionalDocuments n'est pas disponible à cette étape.");
        if (payment.Status == expectedStatus) return Result(payment);
        if (payment.Status != "authorized")
            throw new InvalidOperationException("Le paiement n'est pas dans l'état authorized.");
        if (string.IsNullOrEmpty(payment.StripePaymentIntentId))
            throw new InvalidOperationException("La preuve d'autorisation Stripe est absente.");
        MachineRequestPaymentProof proof;
        if (expectedStatus == "cancelled" && payment.RequestKind == MachineRequestKind.AdditionalMachine)
        {
            var resolver = additionalContextResolver
                ?? throw new InvalidOperationException("Le contexte AdditionalMachine n'est pas configuré.");
            var resolution = await resolver.ResolveStoredPaymentAsync(payment, ct);
            if (!resolution.Success || resolution.Context is null)
                throw new InvalidOperationException("Le contexte AdditionalMachine n'est plus valide.");
            proof = await gateway.CancelAdditionalAsync(payment, resolution.Context, ct);
        }
        else proof = await action(payment, ct);
        if (proof.Status != expectedStatus || proof.PaymentIntentId != payment.StripePaymentIntentId)
            throw new InvalidOperationException("L'opération Stripe n'a pas été confirmée.");
        payment = await store.CompleteAsync(payment, expectedStatus, ct);
        return Result(payment);
    }

    private static MachineRequestPaymentResult Result(MachineRequestPayment payment, string? url = null)
    {
        var displayedCents = payment.Status == "captured" && payment.FinalCaptureAmountCents is not null
            ? payment.FinalCaptureAmountCents.Value
            : payment.AmountCents;
        return new(payment.PaymentRequestId, payment.Status, displayedCents / 100m, payment.Currency, url,
            payment.FinalCaptureAmountCents is null ? null : payment.AmountCents / 100m,
            ProvisioningStage(payment.ProvisioningStage), payment.CompanyId, payment.MachineId,
            payment.ActivatedAtUtc, payment.FirstPeriodEndUtc, payment.ServiceAmountCents,
            payment.PreparationStatus == MachineRequestPreparationStatus.Ready ? "ready" : "pending",
            payment.ReadyAtUtc, payment.ReadyByUserId);
    }

    private static string ProvisioningStage(MachineRequestProvisioningStage stage) => stage switch
    {
        MachineRequestProvisioningStage.AwaitingAcceptance => "awaitingAcceptance",
        MachineRequestProvisioningStage.AmountFinalized => "amountFinalized",
        MachineRequestProvisioningStage.BusinessEntitiesCreated => "businessEntitiesCreated",
        MachineRequestProvisioningStage.CustomerLinked => "customerLinked",
        MachineRequestProvisioningStage.SubscriptionCreated => "subscriptionCreated",
        MachineRequestProvisioningStage.InitialPeriodCreated => "initialPeriodCreated",
        MachineRequestProvisioningStage.Completed => "completed",
        _ => throw new InvalidOperationException("Étape de provisionnement inconnue.")
    };

    private static string? NormalizeEmail(string? email)
    {
        var value = email?.Trim();
        if (string.IsNullOrEmpty(value)) return null;
        if (!System.Net.Mail.MailAddress.TryCreate(value, out var parsed) || parsed.Address != value)
            throw new ArgumentException("Adresse e-mail invalide.", nameof(email));
        return value;
    }

    private static void EnsureAdditionalDocumentsOwnership(MachineRequestPayment payment,
        AdditionalDocumentsContext context)
    {
        if (payment.RequestKind != MachineRequestKind.AdditionalDocuments
            || payment.RequestedByUserId != context.UserId || payment.CompanyId != context.CompanyId
            || payment.TargetMachineId != context.MachineId || string.IsNullOrWhiteSpace(payment.MachineRequestId))
            throw new UnauthorizedAccessException("Cette tentative documentaire n'appartient pas à l'utilisateur authentifié.");
    }

    private static void EnsureAdditionalOwnership(MachineRequestPayment payment,
        AdditionalMachinePaymentContext context, int totalPages, long amountCents)
    {
        if (payment.RequestKind != MachineRequestKind.AdditionalMachine
            || payment.RequestedByUserId != context.UserId || payment.CompanyId != context.CompanyId
            || payment.TotalPages != totalPages || payment.AmountCents != amountCents)
            throw new UnauthorizedAccessException("Ce paiement n'appartient pas à l'utilisateur authentifié.");
    }
}
