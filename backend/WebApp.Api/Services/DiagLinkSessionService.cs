using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using WebApp.Api.Data;

namespace WebApp.Api.Services;

/// <summary>Result of a valid DiagLink session lookup — never exposes TokenHash or the raw token.</summary>
public record DiagLinkSessionResult(Guid UserId, DateTime ExpiresAtUtc);

/// <summary>
/// Validates opaque DiagLink session tokens against chat.UserSessions. Single source of truth for the
/// hash algorithm (HMAC-SHA256 + Auth:OtpPepper, same pepper as the OTP flow) so it is never duplicated
/// between /api/auth/validate-session and future chat-endpoint authentication.
/// </summary>
public class DiagLinkSessionService
{
    private readonly DiagLinkDbContext _db;
    private readonly IConfiguration _configuration;

    public DiagLinkSessionService(DiagLinkDbContext db, IConfiguration configuration)
    {
        _db = db;
        _configuration = configuration;
    }

    public async Task<DiagLinkSessionResult?> ValidateSessionAsync(string? sessionToken, CancellationToken cancellationToken)
    {
        var token = sessionToken?.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var otpPepper = _configuration["Auth:OtpPepper"]
            ?? throw new InvalidOperationException("Auth:OtpPepper is not configured");

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(otpPepper));
        var submittedHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(token));
        var submittedHashHex = Convert.ToHexString(submittedHash);

        var now = DateTime.UtcNow;

        var session = await _db.UserSessions
            .Where(s =>
                s.TokenHash == submittedHashHex &&
                s.RevokedAtUtc == null &&
                s.ExpiresAtUtc > now)
            .FirstOrDefaultAsync(cancellationToken);

        if (session is null)
        {
            return null;
        }

        var storedHash = Convert.FromHexString(session.TokenHash);
        var valid = CryptographicOperations.FixedTimeEquals(submittedHash, storedHash);

        return valid ? new DiagLinkSessionResult(session.UserId, session.ExpiresAtUtc) : null;
    }
}
