using Microsoft.EntityFrameworkCore;
using WebApp.Api.Data;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;
using PaymentEntity = WebApp.Api.Models.Entities.MachineRequestPayment;
using PaymentStatus = WebApp.Api.Models.Entities.MachineRequestPaymentStatus;

namespace WebApp.Api.Services;

public sealed record MachineRequestPayment(Guid PaymentRequestId, int TotalPages, long AmountCents, string Currency,
    string? Email, string? StripeSessionId, string? StripePaymentIntentId, string Status, DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc = null, DateTime? AuthorizedAtUtc = null, DateTime? CapturedAtUtc = null,
    DateTime? CancelledAtUtc = null, string? AuthorizationEventId = null, byte[]? RowVersion = null,
    string? MachineRequestId = null, DateTime? RequestLinkedAtUtc = null,
    DateTime? ActivatedAtUtc = null, DateTime? FirstPeriodEndUtc = null, int? ServiceAmountCents = null,
    long? FinalCaptureAmountCents = null, Guid? CompanyId = null, Guid? MachineId = null,
    MachineRequestProvisioningStage ProvisioningStage = MachineRequestProvisioningStage.AwaitingAcceptance,
    DateTime? ProvisioningCompletedAtUtc = null,
    MachineRequestPreparationStatus PreparationStatus = MachineRequestPreparationStatus.Pending,
    DateTime? ReadyAtUtc = null,
    Guid? ReadyByUserId = null,
    MachineRequestKind RequestKind = MachineRequestKind.InitialMachine,
    Guid? RequestedByUserId = null,
    Guid? TargetMachineId = null);

/// <summary>Durable SQL repository for machine-request payments.</summary>
public sealed class MachineRequestPaymentStore(DiagLinkDbContext db)
{
    public async Task<MachineRequestPayment> AddAsync(MachineRequestPayment payment, CancellationToken ct)
    {
        var entity = ToEntity(payment);
        db.MachineRequestPayments.Add(entity);
        await db.SaveChangesAsync(ct);
        return FromEntity(entity);
    }

    public async Task<MachineRequestPayment> AddOrGetAsync(MachineRequestPayment payment, CancellationToken ct)
    {
        var existing = await GetAsync(payment.PaymentRequestId, ct);
        if (existing is not null) return existing;
        try { return await AddAsync(payment, ct); }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var concurrent = await GetAsync(payment.PaymentRequestId, ct);
            if (concurrent is not null) return concurrent;
            throw;
        }
    }

    public async Task<MachineRequestPayment?> GetAsync(Guid id, CancellationToken ct)
    {
        var entity = await db.MachineRequestPayments.AsNoTracking()
            .SingleOrDefaultAsync(payment => payment.Id == id, ct);
        return entity is null ? null : FromEntity(entity);
    }

    public async Task<MachineRequestPayment> SetCheckoutSessionAsync(MachineRequestPayment payment, string sessionId,
        CancellationToken ct)
    {
        var entity = await db.MachineRequestPayments.SingleAsync(item => item.Id == payment.PaymentRequestId, ct);
        EnsureVersion(entity, payment);
        if (entity.Status != PaymentStatus.Pending || entity.StripeSessionId is not null)
            throw new InvalidOperationException("Le paiement n'est plus en attente de sa session Stripe.");
        entity.StripeSessionId = sessionId;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return FromEntity(entity);
    }

    public async Task RemovePendingAsync(MachineRequestPayment payment, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var entity = await db.MachineRequestPayments.SingleOrDefaultAsync(item => item.Id == payment.PaymentRequestId, ct);
        if (entity is null) return;
        EnsureVersion(entity, payment);
        if (entity.Status != PaymentStatus.Pending) return;
        db.MachineRequestPayments.Remove(entity);
        await db.SaveChangesAsync(ct);
    }

    public async Task<MachineRequestPayment> AbandonPendingAsync(MachineRequestPayment payment, CancellationToken ct)
    {
        var entity = await db.MachineRequestPayments.SingleAsync(item => item.Id == payment.PaymentRequestId, ct);
        if (entity.Status == PaymentStatus.Abandoned) return FromEntity(entity);
        EnsureVersion(entity, payment);
        if (entity.Status != PaymentStatus.Pending)
            throw new InvalidOperationException("Le paiement n'est plus en attente.");
        if (entity.StripePaymentIntentId is not null || entity.AuthorizationEventId is not null
            || entity.AuthorizedAtUtc is not null || entity.CapturedAtUtc is not null)
            throw new InvalidOperationException("Une tentative autorisée ne peut pas être marquée comme abandonnée.");
        var now = DateTime.UtcNow;
        entity.Status = PaymentStatus.Abandoned;
        entity.CancelledAtUtc = now;
        entity.UpdatedAtUtc = now;
        await db.SaveChangesAsync(ct);
        return FromEntity(entity);
    }

    public async Task<string> AuthorizeAsync(MachineRequestPayment payment, string paymentIntentId, string eventId,
        CancellationToken ct)
    {
        var entity = await db.MachineRequestPayments.SingleOrDefaultAsync(item => item.Id == payment.PaymentRequestId, ct);
        if (entity is null) return "unknown";
        if (entity.Status != PaymentStatus.Pending) return Status(entity.Status);
        EnsureVersion(entity, payment);
        var now = DateTime.UtcNow;
        entity.Status = PaymentStatus.Authorized;
        entity.StripePaymentIntentId = paymentIntentId;
        entity.AuthorizationEventId = eventId;
        entity.AuthorizedAtUtc = now;
        entity.UpdatedAtUtc = now;
        try
        {
            await db.SaveChangesAsync(ct);
            return "authorized";
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            var current = await GetAsync(payment.PaymentRequestId, ct);
            return current?.Status ?? "unknown";
        }
        catch (DbUpdateException ex)
        {
            throw new InvalidOperationException("L'événement Stripe a déjà été traité ou contredit une autre autorisation.", ex);
        }
    }

    public async Task<MachineRequestPayment> CompleteAsync(MachineRequestPayment payment, string targetStatus,
        CancellationToken ct)
    {
        var target = ParseFinalStatus(targetStatus);
        var entity = await db.MachineRequestPayments.SingleAsync(item => item.Id == payment.PaymentRequestId, ct);
        if (entity.Status == target) return FromEntity(entity);
        EnsureVersion(entity, payment);
        if (entity.Status != PaymentStatus.Authorized)
            throw new InvalidOperationException("Le paiement n'est pas dans l'état authorized.");
        var now = DateTime.UtcNow;
        entity.Status = target;
        entity.UpdatedAtUtc = now;
        if (target == PaymentStatus.Captured) entity.CapturedAtUtc = now;
        else entity.CancelledAtUtc = now;
        try
        {
            await db.SaveChangesAsync(ct);
            return FromEntity(entity);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            var current = await GetAsync(payment.PaymentRequestId, ct)
                ?? throw new InvalidOperationException("Le paiement est devenu indisponible.");
            if (current.Status == targetStatus) return current;
            throw new InvalidOperationException("Le paiement a été modifié par une opération concurrente.");
        }
    }

    public async Task<MachineRequestPayment> CompleteAdminDecisionAsync(MachineRequestPayment payment,
        string targetStatus, EmailOutboxEnqueue notification, CancellationToken ct)
    {
        var target = ParseFinalStatus(targetStatus);
        var expectedNotification = target == PaymentStatus.Captured
            ? EmailNotificationType.RequestAccepted
            : EmailNotificationType.RequestRejected;
        if (notification.NotificationType != expectedNotification
            || notification.PaymentRequestId != payment.PaymentRequestId
            || string.IsNullOrWhiteSpace(payment.MachineRequestId)
            || !string.Equals(notification.MachineRequestId, payment.MachineRequestId, StringComparison.Ordinal))
            throw new InvalidOperationException("La notification ne correspond pas à la décision financière.");

        // Validate the immutable outbox value before opening the transaction or changing the payment.
        var pendingOutbox = EmailOutboxStore.CreatePending(notification, DateTime.UtcNow);
        async Task<MachineRequestPayment> ExecuteAsync()
        {
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(ct)
                : null;
            try
            {
                var entity = await db.MachineRequestPayments.SingleAsync(item => item.Id == payment.PaymentRequestId, ct);
                ValidateDecisionPayload(entity, target, notification.Payload);
                if (entity.Status != target)
                {
                    EnsureVersion(entity, payment);
                    if (entity.Status != PaymentStatus.Authorized)
                        throw new InvalidOperationException("Le paiement n'est pas dans l'état authorized.");
                    var now = DateTime.UtcNow;
                    entity.Status = target;
                    entity.UpdatedAtUtc = now;
                    if (target == PaymentStatus.Captured) entity.CapturedAtUtc = now;
                    else entity.CancelledAtUtc = now;
                }

                var exists = await db.EmailOutbox.AnyAsync(item =>
                    item.MachineRequestId == pendingOutbox.MachineRequestId
                    && item.NotificationType == expectedNotification, ct);
                if (!exists) db.EmailOutbox.Add(pendingOutbox);
                await db.SaveChangesAsync(ct);
                if (transaction is not null) await transaction.CommitAsync(ct);
                return FromEntity(entity);
            }
            catch (DbUpdateException exception)
            {
                if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
                db.ChangeTracker.Clear();
                var current = await GetAsync(payment.PaymentRequestId, CancellationToken.None);
                var completedByAnotherInstance = current?.Status == targetStatus
                    && await db.EmailOutbox.AsNoTracking().AnyAsync(item =>
                        item.MachineRequestId == pendingOutbox.MachineRequestId
                        && item.NotificationType == expectedNotification, CancellationToken.None);
                return completedByAnotherInstance
                    ? current!
                    : throw new InvalidOperationException(
                        "La décision financière et sa notification n'ont pas pu être enregistrées atomiquement.", exception);
            }
        }

        if (!db.Database.IsRelational()) return await ExecuteAsync();
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(ExecuteAsync);
    }

    private static void ValidateDecisionPayload(PaymentEntity entity, PaymentStatus target, object payload)
    {
        if (target == PaymentStatus.Captured)
        {
            if (payload is not RequestAcceptedEmailPayload accepted)
                throw new InvalidOperationException("Le payload RequestAccepted est invalide.");
            var capturedAmount = entity.RequestKind == MachineRequestKind.AdditionalDocuments
                ? entity.AmountCents
                : entity.FinalCaptureAmountCents
                    ?? throw new InvalidOperationException("Le montant final capturé est absent.");
            if (accepted.CapturedAmountCents != capturedAmount || accepted.Currency != entity.Currency)
                throw new InvalidOperationException("Le payload RequestAccepted ne correspond pas au montant capturé.");
        }
        else if (payload is not RequestRejectedEmailPayload rejected || rejected.Currency != entity.Currency)
            throw new InvalidOperationException("Le payload RequestRejected est invalide.");
    }

    public async Task<MachineRequestPayment> MarkReadyAsync(MachineRequestPayment payment,
        MachineRequestRecord request, Guid readyByUserId, CancellationToken ct)
    {
        if (readyByUserId == Guid.Empty) throw new UnauthorizedAccessException("Le Super Admin est introuvable.");
        if (payment.Status != "captured" || payment.CapturedAtUtc is null
            || string.IsNullOrWhiteSpace(payment.MachineRequestId)
            || request.Status != MachineRequestStatuses.Treated)
            throw new InvalidOperationException("La demande capturée et acceptée est requise.");
        async Task<MachineRequestPayment> ExecuteAsync()
        {
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(ct)
                : null;
            try
            {
                var entity = await db.MachineRequestPayments.SingleAsync(item => item.Id == payment.PaymentRequestId, ct);
                if (entity.Status != PaymentStatus.Captured || entity.CapturedAtUtc is null
                    || entity.MachineRequestId != request.RequestId || entity.RequestKind != request.RequestKind)
                    throw new InvalidOperationException("Le paiement capturé ne correspond pas à la demande.");
                if (entity.PreparationStatus == MachineRequestPreparationStatus.Pending)
                {
                    EnsureVersion(entity, payment);
                    entity.PreparationStatus = MachineRequestPreparationStatus.Ready;
                    entity.ReadyAtUtc = DateTime.UtcNow;
                    entity.ReadyByUserId = readyByUserId;
                    entity.UpdatedAtUtc = entity.ReadyAtUtc.Value;
                }
                else if (entity.ReadyAtUtc is null || entity.ReadyByUserId is null)
                    throw new InvalidOperationException("L'état documentaire Ready est incohérent.");

                var notification = MachineRequestDecisionNotifications.Ready(request, FromEntity(entity), entity.ReadyAtUtc!.Value);
                var expected = entity.RequestKind == MachineRequestKind.AdditionalDocuments
                    ? EmailNotificationType.DocumentsReady : EmailNotificationType.MachineReady;
                if (notification.NotificationType != expected)
                    throw new InvalidOperationException("Le type de notification Ready est incohérent.");
                var exists = await db.EmailOutbox.AnyAsync(item => item.MachineRequestId == entity.MachineRequestId
                    && item.NotificationType == expected, ct);
                if (!exists) db.EmailOutbox.Add(EmailOutboxStore.CreatePending(notification, DateTime.UtcNow));
                await db.SaveChangesAsync(ct);
                if (transaction is not null) await transaction.CommitAsync(ct);
                return FromEntity(entity);
            }
            catch (DbUpdateException exception)
            {
                if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
                db.ChangeTracker.Clear();
                throw new InvalidOperationException("L'état Ready et sa notification n'ont pas pu être enregistrés atomiquement.", exception);
            }
        }

        if (!db.Database.IsRelational()) return await ExecuteAsync();
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(ExecuteAsync);
    }

    public async Task<MachineRequestPayment> FinalizeAmountAsync(MachineRequestPayment payment,
        long preparationAmountCents, DateTime activatedAtUtc, CancellationToken ct)
    {
        var entity = await db.MachineRequestPayments.SingleAsync(item => item.Id == payment.PaymentRequestId, ct);
        if (entity.ProvisioningStage >= MachineRequestProvisioningStage.AmountFinalized)
            return FromEntity(entity);
        EnsureVersion(entity, payment);
        if (entity.Status != PaymentStatus.Authorized)
            throw new InvalidOperationException("Le paiement n'est pas dans l'état authorized.");
        if (!MachineRequestPreparationPricing.IncludesMaximumFirstSubscription(
                entity.EstimatedTotalPages, entity.AmountCents))
            throw new InvalidOperationException("Une demande historique ne doit pas être finalisée avec un abonnement.");
        if (preparationAmountCents <= 0
            || checked(preparationAmountCents + MachineRequestPreparationPricing.MaximumFirstSubscriptionCents) != entity.AmountCents)
            throw new InvalidOperationException("Le coût documentaire serveur ne correspond pas à l'autorisation Stripe.");

        var period = MachineRequestFirstPeriodPricing.Calculate(activatedAtUtc);
        var finalAmount = checked(preparationAmountCents + 1000L + period.ServiceAmountCents);
        if (finalAmount <= 0 || finalAmount > entity.AmountCents)
            throw new InvalidOperationException("Le montant final à capturer est incohérent.");

        entity.ActivatedAtUtc = period.ActivatedAtUtc;
        entity.FirstPeriodEndUtc = period.FirstPeriodEndUtc;
        entity.ServiceAmountCents = period.ServiceAmountCents;
        entity.FinalCaptureAmountCents = finalAmount;
        entity.ProvisioningStage = MachineRequestProvisioningStage.AmountFinalized;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        try
        {
            await db.SaveChangesAsync(ct);
            return FromEntity(entity);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            var current = await GetAsync(payment.PaymentRequestId, ct)
                ?? throw new InvalidOperationException("Le paiement est devenu indisponible.");
            if (current.ProvisioningStage >= MachineRequestProvisioningStage.AmountFinalized)
                return current;
            throw new InvalidOperationException("La finalisation du montant a rencontré une modification concurrente.");
        }
    }

    public async Task<MachineRequestPayment> AttachBusinessEntitiesAsync(MachineRequestPayment payment,
        Guid companyId, Guid machineId, CancellationToken ct)
    {
        var entity = await db.MachineRequestPayments.SingleAsync(item => item.Id == payment.PaymentRequestId, ct);
        if (entity.ProvisioningStage >= MachineRequestProvisioningStage.BusinessEntitiesCreated)
        {
            if (entity.CompanyId == companyId && entity.MachineId == machineId) return FromEntity(entity);
            throw new InvalidOperationException("La destination de provisionnement est déjà figée pour cette demande.");
        }
        EnsureVersion(entity, payment);
        if (entity.Status != PaymentStatus.Captured
            || entity.ProvisioningStage != MachineRequestProvisioningStage.AmountFinalized)
            throw new InvalidOperationException("Le paiement doit être capturé et son montant finalisé avant le rattachement.");
        if (entity.ActivatedAtUtc is null || entity.FirstPeriodEndUtc is null
            || entity.ServiceAmountCents is null || entity.FinalCaptureAmountCents is null)
            throw new InvalidOperationException("Les données financières figées sont incomplètes.");

        if (!await db.Companies.AsNoTracking().AnyAsync(company => company.Id == companyId, ct))
            throw new InvalidOperationException("L'entreprise sélectionnée n'existe pas.");
        var machine = await db.Machines.AsNoTracking().SingleOrDefaultAsync(item => item.Id == machineId, ct)
            ?? throw new InvalidOperationException("La machine sélectionnée n'existe pas.");
        if (machine.CompanyId != companyId)
            throw new InvalidOperationException("La machine sélectionnée n'appartient pas à cette entreprise.");
        if (await db.MachineRequestPayments.AsNoTracking()
            .AnyAsync(item => item.Id != entity.Id && item.MachineId == machineId, ct))
            throw new InvalidOperationException("Cette machine est déjà rattachée à une autre demande initiale.");

        entity.CompanyId = companyId;
        entity.MachineId = machineId;
        entity.ProvisioningStage = MachineRequestProvisioningStage.BusinessEntitiesCreated;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        try
        {
            await db.SaveChangesAsync(ct);
            return FromEntity(entity);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            var current = await GetAsync(payment.PaymentRequestId, ct)
                ?? throw new InvalidOperationException("Le paiement est devenu indisponible.");
            if (current.ProvisioningStage >= MachineRequestProvisioningStage.BusinessEntitiesCreated
                && current.CompanyId == companyId && current.MachineId == machineId)
                return current;
            throw new InvalidOperationException("Une autre destination de provisionnement a été enregistrée simultanément.");
        }
        catch (DbUpdateException ex)
        {
            db.ChangeTracker.Clear();
            if (await db.MachineRequestPayments.AsNoTracking()
                .AnyAsync(item => item.Id != payment.PaymentRequestId && item.MachineId == machineId, ct))
                throw new InvalidOperationException("Cette machine est déjà rattachée à une autre demande initiale.", ex);
            throw;
        }
    }

    public async Task<MachineRequestPayment> CreateAndAttachAdditionalMachineAsync(
        MachineRequestPayment payment, MachineRequestMachine requestedMachine, CancellationToken ct)
    {
        async Task<MachineRequestPayment> ExecuteAsync()
        {
            var entity = await db.MachineRequestPayments.SingleAsync(item => item.Id == payment.PaymentRequestId, ct);
            if (entity.RequestKind != MachineRequestKind.AdditionalMachine || entity.CompanyId is null
                || entity.RequestedByUserId is null)
                throw new InvalidOperationException("La demande AdditionalMachine est incohérente.");
            if (entity.ProvisioningStage >= MachineRequestProvisioningStage.BusinessEntitiesCreated)
            {
                if (entity.MachineId is null) throw new InvalidOperationException("La Machine rattachée est absente.");
                var linked = await db.Machines.AsNoTracking().SingleOrDefaultAsync(machine => machine.Id == entity.MachineId, ct);
                if (linked is null || linked.CompanyId != entity.CompanyId)
                    throw new InvalidOperationException("La Machine rattachée est incohérente.");
                return FromEntity(entity);
            }
            EnsureVersion(entity, payment);
            if (entity.Status != PaymentStatus.Captured
                || entity.ProvisioningStage != MachineRequestProvisioningStage.AmountFinalized)
                throw new InvalidOperationException("La capture confirmée et le montant finalisé sont requis avant de créer la Machine.");
            if (!await db.Companies.AsNoTracking().AnyAsync(company => company.Id == entity.CompanyId
                    && company.Status == "active", ct))
                throw new InvalidOperationException("L'entreprise liée n'existe plus ou n'est plus active.");
            if (entity.MachineId is not null)
                throw new InvalidOperationException("Une Machine est déjà rattachée avant l'étape attendue.");

            var now = DateTime.UtcNow;
            var machine = new Machine
            {
                Id = Guid.NewGuid(), CompanyId = entity.CompanyId.Value,
                Name = requestedMachine.MachineName, Reference = requestedMachine.SerialNumber,
                Status = "active", CreatedAtUtc = now, UpdatedAtUtc = now
            };
            db.Machines.Add(machine);
            entity.MachineId = machine.Id;
            entity.ProvisioningStage = MachineRequestProvisioningStage.BusinessEntitiesCreated;
            entity.UpdatedAtUtc = now;
            await db.SaveChangesAsync(ct);
            return FromEntity(entity);
        }

        if (!db.Database.IsRelational()) return await ExecuteAsync();
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            try
            {
                var result = await ExecuteAsync();
                await transaction.CommitAsync(ct);
                return result;
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        });
    }

    public async Task<MachineRequestPayment> MarkCustomerLinkedAsync(MachineRequestPayment payment,
        string customerId, CancellationToken ct)
    {
        var entity = await db.MachineRequestPayments.SingleAsync(item => item.Id == payment.PaymentRequestId, ct);
        if (entity.Status != PaymentStatus.Captured || entity.CompanyId is null || entity.MachineId is null
            || entity.ActivatedAtUtc is null || entity.FirstPeriodEndUtc is null
            || entity.ServiceAmountCents is null || entity.FinalCaptureAmountCents is null)
            throw new InvalidOperationException("Le paiement capturé, les entités et les données financières figées sont requis.");
        var accountMatches = await db.BillingAccounts.AsNoTracking().AnyAsync(account =>
            account.CompanyId == entity.CompanyId && account.StripeCustomerId == customerId, ct);
        if (!accountMatches) throw new InvalidOperationException("Le BillingAccount ne confirme pas le Customer Stripe attendu.");
        if (entity.ProvisioningStage >= MachineRequestProvisioningStage.CustomerLinked) return FromEntity(entity);
        EnsureVersion(entity, payment);
        if (entity.ProvisioningStage != MachineRequestProvisioningStage.BusinessEntitiesCreated)
            throw new InvalidOperationException("Le rattachement entreprise/machine doit être terminé avant la facturation.");
        entity.ProvisioningStage = MachineRequestProvisioningStage.CustomerLinked;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        try { await db.SaveChangesAsync(ct); return FromEntity(entity); }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            var current = await GetAsync(payment.PaymentRequestId, ct)
                ?? throw new InvalidOperationException("Le paiement est devenu indisponible.");
            if (current.ProvisioningStage >= MachineRequestProvisioningStage.CustomerLinked) return current;
            throw new InvalidOperationException("Le provisionnement a été modifié simultanément.");
        }
    }

    public async Task<MachineRequestPayment> MarkSubscriptionCreatedAsync(MachineRequestPayment payment,
        string subscriptionId, CancellationToken ct)
    {
        var entity = await db.MachineRequestPayments.SingleAsync(item => item.Id == payment.PaymentRequestId, ct);
        if (entity.Status != PaymentStatus.Captured || entity.CompanyId is null || entity.MachineId is null
            || entity.FirstPeriodEndUtc is null || entity.ActivatedAtUtc is null
            || entity.ServiceAmountCents is null || entity.FinalCaptureAmountCents is null)
            throw new InvalidOperationException("Les données figées du provisionnement sont incomplètes.");
        if (!await db.BillingAccounts.AsNoTracking().AnyAsync(account => account.CompanyId == entity.CompanyId
            && account.StripeSubscriptionId == subscriptionId && account.StripeCustomerId != null, ct))
            throw new InvalidOperationException("Le BillingAccount ne confirme pas la Subscription attendue.");
        if (entity.ProvisioningStage >= MachineRequestProvisioningStage.SubscriptionCreated) return FromEntity(entity);
        EnsureVersion(entity, payment);
        if (entity.ProvisioningStage != MachineRequestProvisioningStage.CustomerLinked)
            throw new InvalidOperationException("Le Customer doit être rattaché avant de configurer l'abonnement.");
        entity.ProvisioningStage = MachineRequestProvisioningStage.SubscriptionCreated;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        try { await db.SaveChangesAsync(ct); return FromEntity(entity); }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            var current = await GetAsync(payment.PaymentRequestId, ct)
                ?? throw new InvalidOperationException("Le paiement est devenu indisponible.");
            if (current.ProvisioningStage >= MachineRequestProvisioningStage.SubscriptionCreated) return current;
            throw new InvalidOperationException("Le provisionnement a été modifié simultanément.");
        }
    }

    public Task<MachineRequestPayment> MarkInitialPeriodCreatedAsync(MachineRequestPayment payment, CancellationToken ct) =>
        AdvanceProvisioningAsync(payment, MachineRequestProvisioningStage.SubscriptionCreated,
            MachineRequestProvisioningStage.InitialPeriodCreated, false, ct);

    public Task<MachineRequestPayment> CompleteProvisioningAsync(MachineRequestPayment payment, DateTime completedAtUtc,
        CancellationToken ct) => AdvanceProvisioningAsync(payment, MachineRequestProvisioningStage.InitialPeriodCreated,
            MachineRequestProvisioningStage.Completed, true, ct, completedAtUtc);

    private async Task<MachineRequestPayment> AdvanceProvisioningAsync(MachineRequestPayment payment,
        MachineRequestProvisioningStage expected, MachineRequestProvisioningStage target, bool complete,
        CancellationToken ct, DateTime? completedAtUtc = null)
    {
        var entity = await db.MachineRequestPayments.SingleAsync(item => item.Id == payment.PaymentRequestId, ct);
        if (entity.ProvisioningStage >= target)
        {
            if (complete && entity.ProvisioningCompletedAtUtc is null)
                throw new InvalidOperationException("Le checkpoint Completed est incomplet.");
            return FromEntity(entity);
        }
        EnsureVersion(entity, payment);
        if (entity.Status != PaymentStatus.Captured || entity.ProvisioningStage != expected)
            throw new InvalidOperationException("Le checkpoint précédent du provisionnement est requis.");
        if (complete && (completedAtUtc is null || completedAtUtc.Value.Kind != DateTimeKind.Utc))
            throw new InvalidOperationException("Une date UTC de fin de provisionnement est requise.");
        entity.ProvisioningStage = target;
        if (complete) entity.ProvisioningCompletedAtUtc = completedAtUtc;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        try { await db.SaveChangesAsync(ct); return FromEntity(entity); }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            var current = await GetAsync(payment.PaymentRequestId, ct)
                ?? throw new InvalidOperationException("Le paiement est devenu indisponible.");
            if (current.ProvisioningStage >= target) return current;
            throw new InvalidOperationException("Le provisionnement a été modifié simultanément.");
        }
    }

    public async Task<MachineRequestPayment> ReserveMachineRequestAsync(MachineRequestPayment payment,
        CancellationToken ct)
    {
        var entity = await db.MachineRequestPayments.SingleAsync(item => item.Id == payment.PaymentRequestId, ct);
        if (entity.Status != PaymentStatus.Authorized)
            throw new InvalidOperationException("Le paiement n'est pas dans l'état authorized.");
        if (entity.MachineRequestId is not null) return FromEntity(entity);
        EnsureVersion(entity, payment);
        entity.MachineRequestId = payment.PaymentRequestId.ToString("N");
        entity.RequestLinkedAtUtc = DateTime.UtcNow;
        entity.UpdatedAtUtc = entity.RequestLinkedAtUtc.Value;
        try
        {
            await db.SaveChangesAsync(ct);
            return FromEntity(entity);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            var current = await GetAsync(payment.PaymentRequestId, ct)
                ?? throw new InvalidOperationException("Le paiement est devenu indisponible.");
            if (current.MachineRequestId is not null) return current;
            throw new InvalidOperationException("Le paiement a été modifié par une opération concurrente.");
        }
    }

    private static void EnsureVersion(PaymentEntity entity, MachineRequestPayment payment)
    {
        if (!(entity.RowVersion ?? []).SequenceEqual(payment.RowVersion ?? []))
            throw new InvalidOperationException("Le paiement a été modifié par une autre opération.");
    }

    private static PaymentStatus ParseFinalStatus(string status) => status switch
    {
        "captured" => PaymentStatus.Captured,
        "cancelled" => PaymentStatus.Cancelled,
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    private static string Status(PaymentStatus status) => status switch
    {
        PaymentStatus.Pending => "pending",
        PaymentStatus.Authorized => "authorized",
        PaymentStatus.Captured => "captured",
        PaymentStatus.Cancelled => "cancelled",
        PaymentStatus.Abandoned => "abandoned",
        _ => throw new InvalidOperationException("Statut de paiement inconnu.")
    };

    private static PaymentEntity ToEntity(MachineRequestPayment payment) => new()
    {
        Id = payment.PaymentRequestId,
        RequestKind = payment.RequestKind,
        RequestedByUserId = payment.RequestedByUserId,
        Status = PaymentStatus.Pending,
        EstimatedTotalPages = payment.TotalPages,
        AmountCents = payment.AmountCents,
        Currency = payment.Currency,
        Email = payment.Email,
        StripeSessionId = payment.StripeSessionId,
        StripePaymentIntentId = payment.StripePaymentIntentId,
        AuthorizationEventId = payment.AuthorizationEventId,
        MachineRequestId = payment.MachineRequestId,
        CreatedAtUtc = payment.CreatedAtUtc,
        UpdatedAtUtc = payment.UpdatedAtUtc ?? payment.CreatedAtUtc,
        AuthorizedAtUtc = payment.AuthorizedAtUtc,
        CapturedAtUtc = payment.CapturedAtUtc,
        CancelledAtUtc = payment.CancelledAtUtc,
        RequestLinkedAtUtc = payment.RequestLinkedAtUtc,
        ActivatedAtUtc = payment.ActivatedAtUtc,
        FirstPeriodEndUtc = payment.FirstPeriodEndUtc,
        ServiceAmountCents = payment.ServiceAmountCents,
        FinalCaptureAmountCents = payment.FinalCaptureAmountCents,
        CompanyId = payment.CompanyId,
        MachineId = payment.MachineId,
        TargetMachineId = payment.TargetMachineId,
        ProvisioningStage = payment.ProvisioningStage,
        ProvisioningCompletedAtUtc = payment.ProvisioningCompletedAtUtc,
        PreparationStatus = payment.PreparationStatus,
        ReadyAtUtc = payment.ReadyAtUtc,
        ReadyByUserId = payment.ReadyByUserId
    };

    private static MachineRequestPayment FromEntity(PaymentEntity payment) => new(payment.Id,
        payment.EstimatedTotalPages, payment.AmountCents, payment.Currency, payment.Email, payment.StripeSessionId,
        payment.StripePaymentIntentId, Status(payment.Status), payment.CreatedAtUtc, payment.UpdatedAtUtc,
        payment.AuthorizedAtUtc, payment.CapturedAtUtc, payment.CancelledAtUtc, payment.AuthorizationEventId,
        payment.RowVersion, payment.MachineRequestId, payment.RequestLinkedAtUtc,
        payment.ActivatedAtUtc, payment.FirstPeriodEndUtc, payment.ServiceAmountCents,
        payment.FinalCaptureAmountCents, payment.CompanyId, payment.MachineId,
        payment.ProvisioningStage, payment.ProvisioningCompletedAtUtc,
        payment.PreparationStatus, payment.ReadyAtUtc, payment.ReadyByUserId,
        payment.RequestKind, payment.RequestedByUserId, payment.TargetMachineId);
}
