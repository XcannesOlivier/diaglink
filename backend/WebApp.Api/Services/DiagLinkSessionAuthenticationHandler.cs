using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace WebApp.Api.Services;

/// <summary>Scheme/header constants for the DiagLink session authentication mechanism.</summary>
public static class DiagLinkAuthenticationDefaults
{
    public const string Scheme = "DiagLinkSession";
    public const string HeaderName = "X-DiagLink-Session";
}

/// <summary>
/// Authenticates requests carrying a valid DiagLink session token in the X-DiagLink-Session header.
/// Registered as an additional scheme alongside the existing Microsoft Identity Web (JWT bearer) scheme.
/// See DiagLinkSessionService for the opaque-token lookup (unchanged) and DiagLinkUserLookupService for
/// the dbo.Users Role/CompanyId/Status resolution re-run on every request — a session token surviving
/// its 24h lifetime must not outlive the user's active status, role, or company assignment.
/// </summary>
public class DiagLinkSessionAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly DiagLinkSessionService _sessionService;
    private readonly DiagLinkUserLookupService _userLookupService;

    public DiagLinkSessionAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        DiagLinkSessionService sessionService,
        DiagLinkUserLookupService userLookupService)
        : base(options, logger, encoder)
    {
        _sessionService = sessionService;
        _userLookupService = userLookupService;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(DiagLinkAuthenticationDefaults.HeaderName, out var headerValues))
        {
            return AuthenticateResult.NoResult();
        }

        var token = headerValues.ToString();
        if (string.IsNullOrWhiteSpace(token))
        {
            return AuthenticateResult.NoResult();
        }

        var sessionResult = await _sessionService.ValidateSessionAsync(token, Context.RequestAborted);
        if (sessionResult is null)
        {
            return AuthenticateResult.Fail("Invalid or expired DiagLink session.");
        }

        // Only dbo.Users is trusted for Role/CompanyId/Email — never the session token itself.
        var user = await _userLookupService.FindActiveUserByIdAsync(sessionResult.UserId, Context.RequestAborted);
        if (user is null)
        {
            return AuthenticateResult.Fail("DiagLink user is no longer active.");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(DiagLinkClaimTypes.UserId, user.Id.ToString()),
            new(ClaimTypes.Role, user.Role),
            new(DiagLinkClaimTypes.CompanyId, user.CompanyId.ToString()),
        };
        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            claims.Add(new Claim(ClaimTypes.Email, user.Email));
        }

        var identity = new ClaimsIdentity(claims, DiagLinkAuthenticationDefaults.Scheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, DiagLinkAuthenticationDefaults.Scheme);

        return AuthenticateResult.Success(ticket);
    }
}
