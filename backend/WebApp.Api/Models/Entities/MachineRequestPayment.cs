using WebApp.Api.Models;

namespace WebApp.Api.Models.Entities;

public enum MachineRequestPaymentStatus
{
    Pending = 0,
    Authorized = 1,
    Captured = 2,
    Cancelled = 3,
    Abandoned = 4
}

public enum MachineRequestProvisioningStage
{
    AwaitingAcceptance = 0,
    AmountFinalized = 1,
    BusinessEntitiesCreated = 2,
    CustomerLinked = 3,
    SubscriptionCreated = 4,
    InitialPeriodCreated = 5,
    Completed = 6
}

public enum MachineRequestPreparationStatus
{
    Pending = 0,
    Ready = 1
}

public class MachineRequestPayment
{
    public Guid Id { get; set; }
    public MachineRequestKind RequestKind { get; set; } = MachineRequestKind.InitialMachine;
    public Guid? RequestedByUserId { get; set; }
    public MachineRequestPaymentStatus Status { get; set; }
    public int EstimatedTotalPages { get; set; }
    public long AmountCents { get; set; }
    public string Currency { get; set; } = "EUR";
    public string? Email { get; set; }
    public string? StripeSessionId { get; set; }
    public string? StripePaymentIntentId { get; set; }
    public string? AuthorizationEventId { get; set; }
    public string? MachineRequestId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? AuthorizedAtUtc { get; set; }
    public DateTime? CapturedAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public DateTime? RequestLinkedAtUtc { get; set; }
    public DateTime? ActivatedAtUtc { get; set; }
    public DateTime? FirstPeriodEndUtc { get; set; }
    public int? ServiceAmountCents { get; set; }
    public long? FinalCaptureAmountCents { get; set; }
    public Guid? CompanyId { get; set; }
    public Guid? MachineId { get; set; }
    public Guid? TargetMachineId { get; set; }
    public MachineRequestProvisioningStage ProvisioningStage { get; set; }
    public DateTime? ProvisioningCompletedAtUtc { get; set; }
    public MachineRequestPreparationStatus PreparationStatus { get; set; }
    public DateTime? ReadyAtUtc { get; set; }
    public Guid? ReadyByUserId { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
