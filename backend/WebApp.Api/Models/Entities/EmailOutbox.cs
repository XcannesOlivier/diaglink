namespace WebApp.Api.Models.Entities;

public enum EmailNotificationType
{
    RequestReceived = 0,
    RequestAccepted = 1,
    RequestRejected = 2,
    MachineReady = 3,
    DocumentsReady = 4
}

public enum EmailOutboxStatus
{
    Pending = 0,
    Sent = 1
}

public class EmailOutbox
{
    public Guid Id { get; set; }
    public string MachineRequestId { get; set; } = "";
    public Guid? PaymentRequestId { get; set; }
    public EmailNotificationType NotificationType { get; set; }
    public string RecipientEmail { get; set; } = "";
    public string? RecipientName { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public EmailOutboxStatus Status { get; set; }
    public int AttemptCount { get; set; }
    public DateTime NextAttemptAtUtc { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTime? LockedUntilUtc { get; set; }
    public DateTime? LastAttemptAtUtc { get; set; }
    public DateTime? SentAtUtc { get; set; }
    public string? ProviderOperationId { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
