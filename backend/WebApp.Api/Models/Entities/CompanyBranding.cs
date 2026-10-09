namespace WebApp.Api.Models.Entities;

/// <summary>Optional visual branding for a DiagLink client company.</summary>
public class CompanyBranding
{
    public Guid CompanyId { get; set; }
    public string? AccentColor { get; set; }
    public string? LogoBlobName { get; set; }
    public string? LogoContentType { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
