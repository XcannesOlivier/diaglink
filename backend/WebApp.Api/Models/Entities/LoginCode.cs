namespace WebApp.Api.Models.Entities;

/// <summary>
/// A one-time login code (6-digit OTP), hashed at rest via HMAC-SHA256 with a server-side pepper —
/// the plaintext code is never persisted. Valid for 10 minutes and single-use (see UsedAtUtc).
/// Requesting a new code for the same UserId must invalidate prior unused codes (set UsedAtUtc),
/// so at most one code is usable at a time per user. Distinct from the future 24h session concept:
/// this table only proves "the user received this email" at a point in time.
/// </summary>
public class LoginCode
{
    public Guid Id { get; set; }
    public required Guid UserId { get; set; }
    public required string CodeHash { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
