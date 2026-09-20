using System.Data;
using Microsoft.EntityFrameworkCore;
using Stripe;
using WebApp.Api.Data;
using WebApp.Api.Models.Entities;

namespace WebApp.Api.Services;

public record StripeAdditionWebhookResult(string Status, int HttpStatus = 200);

/// <summary>Signed payment notification only. No payment initiation or subscription mutation.</summary>
public sealed class StripeMachineAdditionWebhook(DbContextOptions<DiagLinkDbContext> options,
    StripeBillingOptions settings, StripeMachineAdditionService additions,
    ILogger<StripeMachineAdditionWebhook> logger)
{
    public static async Task<IResult> HandleHttpAsync(HttpRequest request,
        StripeMachineAdditionWebhook webhook, CancellationToken ct)
    {
        // Read the unmodified UTF-8 body; never bind/reserialize JSON before signature verification.
        using var reader = new StreamReader(request.Body, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: false);
        var payload = await reader.ReadToEndAsync(ct);
        var result = await webhook.HandleAsync(payload, request.Headers["Stripe-Signature"].ToString(), ct);
        return Results.Json(new { result.Status }, statusCode: result.HttpStatus);
    }

    public async Task<StripeAdditionWebhookResult> HandleAsync(string payload, string signature, CancellationToken ct = default)
    {
        if (!StripeAdminEndpoints.TestActionsEnabled(settings)) return Report(null, "WebhookDisabled", 503);
        if (!settings.WebhookSecret.StartsWith("whsec_", StringComparison.Ordinal))
            return Report(null, "WebhookNotConfigured", 503);

        Event notification;
        try
        {
            // SDK default: 300-second signature tolerance and API version compatibility check.
            notification = EventUtility.ConstructEvent(payload, signature, settings.WebhookSecret);
        }
        catch (Exception ex) when (ex is StripeException or System.Text.Json.JsonException or ArgumentException)
        { return Report(null, "InvalidSignatureOrPayload", 400); }

        if (notification.Type != "invoice.payment_succeeded") return Report(notification.Id, "Ignored");
        var liveKey = settings.SecretKey.StartsWith("sk_live_", StringComparison.Ordinal)
            || settings.SecretKey.StartsWith("rk_live_", StringComparison.Ordinal);
        if (notification.Livemode != liveKey || !string.IsNullOrEmpty(notification.Account)
            || string.IsNullOrEmpty(notification.Id) || !notification.Id.StartsWith("evt_", StringComparison.Ordinal)
            || notification.Id.Length > 200 || notification.Data?.Object is not Invoice invoice
            || string.IsNullOrEmpty(invoice.Id))
            return Report(notification.Id, "InvalidEvent", 400);

        try
        {
            StripeMachineAddition? op;
            await using (var db = new DiagLinkDbContext(options))
                op = await db.StripeMachineAdditions.AsNoTracking().SingleOrDefaultAsync(a => a.StripeInvoiceId == invoice.Id, ct);
            if (op == null) return Report(notification.Id, "UnknownInvoice");

            var status = await BindEventAsync(op, invoice, notification.Id, ct);
            if (status != "Resume") return Report(notification.Id, status, status == "OperationNotReady" ? 503 : 200);

            // Existing gateway re-reads the invoice and settled InvoicePayments from Stripe.
            // It persists the verified reference/paid_at before period creation; payload alone grants no budget.
            var result = await additions.ResumeInvoicePaymentAsync(op.Id, ct);
            return Report(notification.Id, result.Status, result.Status is "AwaitingPayment" or "OperationNotReady" ? 503 : 200);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // No success acknowledgement on transient failure: Stripe must retry the SAME event.
            // Do not log bodies, signatures, secrets or raw provider exceptions.
            logger.LogError("Stripe addition event {EventId} failed ({ErrorType}); retry required", notification.Id, ex.GetType().Name);
            return new("RetryRequired", 503);
        }
    }

    private async Task<string> BindEventAsync(StripeMachineAddition candidate, Invoice invoice, string eventId, CancellationToken ct)
    {
        await using var strategyDb = new DiagLinkDbContext(options);
        return await strategyDb.Database.CreateExecutionStrategy().ExecuteAsync(async token =>
        {
            await using var db = new DiagLinkDbContext(options);
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            var companies = db.Database.IsSqlServer()
                ? db.Companies.FromSqlInterpolated($"SELECT * FROM [dbo].[Companies] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {candidate.CompanyId}")
                : db.Companies.AsQueryable();
            if (!await companies.AnyAsync(c => c.Id == candidate.CompanyId, token)) return "ReconciliationRequired";
            var op = await db.StripeMachineAdditions.SingleAsync(a => a.Id == candidate.Id, token);
            if (await db.StripeMachineAdditions.AnyAsync(a => a.ExternalEventId == eventId && a.Id != op.Id, token))
                return "ReconciliationRequired";
            if (op.Stage == StripeMachineAdditionStage.Completed) return "AlreadyCompleted";
            var account = await db.BillingAccounts.SingleOrDefaultAsync(a => a.Id == op.BillingAccountId, token);
            var machine = await db.Machines.SingleOrDefaultAsync(m => m.Id == op.MachineId, token);
            var expected = (long)op.AiAmountCents + op.ServiceAmountCents;
            if (account?.CompanyId != op.CompanyId || machine?.CompanyId != op.CompanyId
                || account.StripeCustomerId != op.StripeCustomerId || account.StripeSubscriptionId != op.StripeSubscriptionId
                || invoice.Id != op.StripeInvoiceId || invoice.CustomerId != op.StripeCustomerId || invoice.Currency != "eur"
                || invoice.Metadata?.GetValueOrDefault("diaglink_addition_id") != op.Id.ToString()
                || invoice.Metadata?.GetValueOrDefault("diaglink_machine_id") != op.MachineId.ToString()
                || invoice.Metadata?.GetValueOrDefault("diaglink_subscription_id") != op.StripeSubscriptionId
                || invoice.Subtotal != expected || invoice.TotalExcludingTax != expected || invoice.Total < expected)
                return "ReconciliationRequired";
            if (invoice.Status != "paid" || invoice.AmountRemaining != 0 || invoice.AmountPaid < invoice.Total
                || invoice.StatusTransitions?.PaidAt == null) return "PaymentIncomplete";
            // The dedicated resume path can recover a lost finalization checkpoint without Stripe writes.
            if (op.Stage < StripeMachineAdditionStage.StripeQuantityUpdated) return "OperationNotReady";
            op.ExternalEventId ??= eventId;
            await db.SaveChangesAsync(token);
            await tx.CommitAsync(token);
            return "Resume";
        }, ct);
    }

    private StripeAdditionWebhookResult Report(string? eventId, string status, int httpStatus = 200)
    {
        logger.LogInformation("Stripe machine addition webhook {EventId}: {Status}", eventId, status);
        return new(status, httpStatus);
    }
}
