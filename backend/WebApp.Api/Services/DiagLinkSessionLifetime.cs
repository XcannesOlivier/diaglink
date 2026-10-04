using WebApp.Api.Models;

namespace WebApp.Api.Services;

public static class DiagLinkSessionLifetime
{
    public static DateTime GetExpiresAtUtc(DateTime createdAtUtc, string? role) =>
        role is DiagLinkRoles.SuperAdmin or DiagLinkRoles.CompanyAdmin
            ? createdAtUtc.AddMonths(1)
            : createdAtUtc.AddHours(24);
}
