using Microsoft.AspNetCore.Authorization;

namespace WebApp.Api.Services;

/// <summary>
/// Satisfied by either a valid DiagLink session (scheme DiagLinkSession) or a Microsoft JWT bearer
/// principal carrying the Chat.ReadWrite scope — the two authentication paths accepted by chat endpoints.
/// </summary>
public class ChatAccessRequirement : IAuthorizationRequirement
{
    public const string RequiredScope = "Chat.ReadWrite";
}

public class ChatAccessAuthorizationHandler : AuthorizationHandler<ChatAccessRequirement>
{
    // Claim types Microsoft Identity Web reads scopes from (delegated permissions).
    private static readonly string[] ScopeClaimTypes =
    [
        "http://schemas.microsoft.com/identity/claims/scope",
        "scp"
    ];

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ChatAccessRequirement requirement)
    {
        // With two AuthenticationSchemes on the same policy, context.User can carry multiple
        // ClaimsIdentity instances — inspect Identities explicitly rather than the ambient Identity.
        var diagLinkIdentity = context.User.Identities.FirstOrDefault(identity =>
            identity.IsAuthenticated && string.Equals(identity.AuthenticationType, DiagLinkAuthenticationDefaults.Scheme, StringComparison.Ordinal));
        if (diagLinkIdentity is not null)
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var microsoftIdentity = context.User.Identities.FirstOrDefault(identity =>
            identity.IsAuthenticated && !string.Equals(identity.AuthenticationType, DiagLinkAuthenticationDefaults.Scheme, StringComparison.Ordinal));
        if (microsoftIdentity is not null)
        {
            foreach (var claimType in ScopeClaimTypes)
            {
                var scopeClaim = microsoftIdentity.FindFirst(claimType);
                if (scopeClaim is not null &&
                    scopeClaim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(ChatAccessRequirement.RequiredScope))
                {
                    context.Succeed(requirement);
                    return Task.CompletedTask;
                }
            }
        }

        return Task.CompletedTask;
    }
}
