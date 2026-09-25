using WebApp.Api.Models.Entities;

namespace WebApp.Api.Services;

public sealed class EmailOutboxProcessor(
    EmailOutboxStore outbox,
    ITransactionalEmailSender sender,
    ILogger<EmailOutboxProcessor> logger)
{
    public const int BatchSize = 10;
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);

    public async Task<int> ProcessOnceAsync(CancellationToken ct = default)
    {
        var claimed = await outbox.ClaimEligibleAsync(BatchSize, LeaseDuration, ct);
        foreach (var item in claimed)
        {
            try
            {
                var content = RequestReceivedEmailTemplate.Build(item.Entry.NotificationType, item.Entry.PayloadJson);
                var operationId = await sender.SendAsync(item.Entry.RecipientEmail, content.Subject,
                    content.TextBody, content.HtmlBody, ct);
                await outbox.MarkSentAsync(item.Entry.Id, item.LeaseId, operationId, ct);
                logger.LogInformation("Email outbox {OutboxId} sent for request {MachineRequestId} ({NotificationType})",
                    item.Entry.Id, item.Entry.MachineRequestId, item.Entry.NotificationType);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                logger.LogWarning(exception,
                    "Email outbox {OutboxId} attempt {AttemptCount} failed ({NotificationType})",
                    item.Entry.Id, item.Entry.AttemptCount + 1, item.Entry.NotificationType);
                await outbox.RescheduleAfterFailureAsync(item.Entry.Id, item.LeaseId,
                    $"{exception.GetType().Name}: delivery failed", CancellationToken.None);
            }
        }
        return claimed.Count;
    }
}

public sealed class EmailOutboxWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<EmailOutboxWorker> logger) : BackgroundService
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan ReconciliationInterval = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextReconciliation = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                if (DateTimeOffset.UtcNow >= nextReconciliation)
                {
                    await scope.ServiceProvider.GetRequiredService<RequestReceivedNotificationService>()
                        .ReconcileAsync(ct: stoppingToken);
                    nextReconciliation = DateTimeOffset.UtcNow.Add(ReconciliationInterval);
                }
                await scope.ServiceProvider.GetRequiredService<EmailOutboxProcessor>()
                    .ProcessOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Email outbox worker cycle failed");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }
}
