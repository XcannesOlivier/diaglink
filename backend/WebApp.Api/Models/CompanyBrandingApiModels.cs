using System.Text.Json;
using System.Text.Json.Serialization;
using WebApp.Api.Models.Entities;

namespace WebApp.Api.Models;

public sealed record UpdateCompanyBrandingRequest
{
    public string? AccentColor { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }
}

public sealed record CompanyBrandingResponse(string? AccentColor, bool HasLogo)
{
    public static CompanyBrandingResponse From(CompanyBranding? branding) => new(
        branding?.AccentColor,
        !string.IsNullOrWhiteSpace(branding?.LogoBlobName));
}
