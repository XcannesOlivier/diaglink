using WebApp.Api.Models.Entities;

namespace WebApp.Api.Services;

public interface ICompanyBrandingService
{
    Task<CompanyBranding?> GetAsync(Guid companyId, CancellationToken cancellationToken = default);
    Task<CompanyBranding?> SetAccentColorAsync(Guid companyId, string? accentColor, CancellationToken cancellationToken = default);
    Task<CompanyBranding> SetLogoMetadataAsync(Guid companyId, string logoBlobName, string logoContentType, CancellationToken cancellationToken = default);
    Task<CompanyBranding?> ClearLogoMetadataAsync(Guid companyId, CancellationToken cancellationToken = default);
    Task ResetAsync(Guid companyId, CancellationToken cancellationToken = default);
}
