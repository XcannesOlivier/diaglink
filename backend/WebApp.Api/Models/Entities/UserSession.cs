namespace WebApp.Api.Models.Entities;

/// <summary>
/// A DiagLink session issued after a successful OTP verification, with a role-based lifetime. The opaque session
/// token is never persisted — only its hash (TokenHash) is stored, checked via constant-time
/// comparison. RevokedAtUtc allows invalidating a session before its natural expiry.
/// </summary>
public class UserSession
{
    public Guid Id { get; set; }
    public required Guid UserId { get; set; }
    public required string TokenHash { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
}
