using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using WebApp.Api.Data;
using WebApp.Api.Models.Entities;

namespace WebApp.Api.Services;

public sealed partial class CompanyBrandingService : ICompanyBrandingService
{
    private const int MaxLogoBlobNameLength = 512;
    private const int MaxLogoContentTypeLength = 100;

    private readonly DiagLinkDbContext _db;
    private readonly TimeProvider _timeProvider;

    public CompanyBrandingService(DiagLinkDbContext db) : this(db, TimeProvider.System)
    {
    }

    public CompanyBrandingService(DiagLinkDbContext db, TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public Task<CompanyBranding?> GetAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        ValidateCompanyId(companyId);
        return _db.CompanyBrandings.AsNoTracking()
            .SingleOrDefaultAsync(branding => branding.CompanyId == companyId, cancellationToken);
    }

    public async Task<CompanyBranding?> SetAccentColorAsync(
        Guid companyId,
        string? accentColor,
        CancellationToken cancellationToken = default)
    {
        ValidateCompanyId(companyId);
        var normalizedColor = NormalizeAccentColor(accentColor);
        var branding = await _db.CompanyBrandings
            .SingleOrDefaultAsync(item => item.CompanyId == companyId, cancellationToken);

        if (branding is null)
        {
            if (normalizedColor is null)
            {
                return null;
            }

            await EnsureCompanyExistsAsync(companyId, cancellationToken);
            branding = new CompanyBranding { CompanyId = companyId };
            _db.CompanyBrandings.Add(branding);
        }

        branding.AccentColor = normalizedColor;
        if (!HasCustomization(branding))
        {
            _db.CompanyBrandings.Remove(branding);
            await _db.SaveChangesAsync(cancellationToken);
            return null;
        }

        branding.UpdatedAtUtc = UtcNow();
        await _db.SaveChangesAsync(cancellationToken);
        return branding;
    }

    public async Task<CompanyBranding> SetLogoMetadataAsync(
        Guid companyId,
        string logoBlobName,
        string logoContentType,
        CancellationToken cancellationToken = default)
    {
        ValidateCompanyId(companyId);
        ValidateLogoMetadata(logoBlobName, logoContentType);

        var branding = await _db.CompanyBrandings
            .SingleOrDefaultAsync(item => item.CompanyId == companyId, cancellationToken);
        if (branding is null)
        {
            await EnsureCompanyExistsAsync(companyId, cancellationToken);
            branding = new CompanyBranding { CompanyId = companyId };
            _db.CompanyBrandings.Add(branding);
        }

        branding.LogoBlobName = logoBlobName;
        branding.LogoContentType = logoContentType;
        branding.UpdatedAtUtc = UtcNow();
        await _db.SaveChangesAsync(cancellationToken);
        return branding;
    }

    public async Task<CompanyBranding?> ClearLogoMetadataAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        ValidateCompanyId(companyId);
        var branding = await _db.CompanyBrandings
            .SingleOrDefaultAsync(item => item.CompanyId == companyId, cancellationToken);
        if (branding is null)
        {
            return null;
        }

        branding.LogoBlobName = null;
        branding.LogoContentType = null;
        if (!HasCustomization(branding))
        {
            _db.CompanyBrandings.Remove(branding);
            await _db.SaveChangesAsync(cancellationToken);
            return null;
        }

        branding.UpdatedAtUtc = UtcNow();
        await _db.SaveChangesAsync(cancellationToken);
        return branding;
    }

    public async Task ResetAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        ValidateCompanyId(companyId);
        var branding = await _db.CompanyBrandings
            .SingleOrDefaultAsync(item => item.CompanyId == companyId, cancellationToken);
        if (branding is null)
        {
            return;
        }

        _db.CompanyBrandings.Remove(branding);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureCompanyExistsAsync(Guid companyId, CancellationToken cancellationToken)
    {
        if (!await _db.Companies.AsNoTracking().AnyAsync(company => company.Id == companyId, cancellationToken))
        {
            throw new ArgumentException("Company does not exist.", nameof(companyId));
        }
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    private static bool HasCustomization(CompanyBranding branding) =>
        branding.AccentColor is not null || branding.LogoBlobName is not null;

    private static string? NormalizeAccentColor(string? accentColor)
    {
        if (accentColor is null)
        {
            return null;
        }

        if (!AccentColorPattern().IsMatch(accentColor))
        {
            throw new ArgumentException("Accent color must use the canonical #RRGGBB format.", nameof(accentColor));
        }

        return accentColor.ToUpperInvariant();
    }

    private static void ValidateCompanyId(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }
    }

    private static void ValidateLogoMetadata(string logoBlobName, string logoContentType)
    {
        if (string.IsNullOrWhiteSpace(logoBlobName) || logoBlobName.Length > MaxLogoBlobNameLength ||
            logoBlobName.StartsWith('/') || logoBlobName.StartsWith('\\') || logoBlobName.Contains('\\') ||
            logoBlobName.Split('/').Any(segment => segment is "" or "." or "..") ||
            logoBlobName.Any(char.IsControl) || Uri.TryCreate(logoBlobName, UriKind.Absolute, out _))
        {
            throw new ArgumentException("Logo blob name must be a valid internal blob key.", nameof(logoBlobName));
        }

        if (string.IsNullOrWhiteSpace(logoContentType) || logoContentType.Length > MaxLogoContentTypeLength ||
            !logoContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
            logoContentType.Any(char.IsControl))
        {
            throw new ArgumentException("Logo content type must be a valid image media type.", nameof(logoContentType));
        }
    }

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex AccentColorPattern();
}
