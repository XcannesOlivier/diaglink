namespace WebApp.Api.Models.Entities;

/// <summary>
/// Grants a technician access to one machine. UserId is a logical reference to dbo.Users.Id only —
/// no SQL FK (dbo.Users is excluded from this app's migrations, same pattern as LoginCode/UserSession).
/// </summary>
public class UserMachineAccess
{
    public Guid UserId { get; set; }
    public Guid MachineId { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public Machine? Machine { get; set; }
}
