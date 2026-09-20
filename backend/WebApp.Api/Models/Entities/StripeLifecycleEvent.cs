namespace WebApp.Api.Models.Entities;
public class StripeLifecycleEvent
{
    public string Id { get; set; } = "";
    public Guid CompanyId { get; set; }
    public string SubscriptionId { get; set; } = "";
    public string EventType { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public Guid? MachineId { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}
