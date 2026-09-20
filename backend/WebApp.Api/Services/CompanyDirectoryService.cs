using Microsoft.EntityFrameworkCore;
using WebApp.Api.Data;
using WebApp.Api.Models.Entities;

namespace WebApp.Api.Services;

/// <summary>Read-only company/company-users queries backing the /api/companies, /api/company and
/// /api/company/users endpoints. Callers are responsible for resolving the CompanyId from claims —
/// this service never applies authorization itself, it only queries what it's given.</summary>
public class CompanyDirectoryService
{
    private readonly DiagLinkDbContext _db;

    public CompanyDirectoryService(DiagLinkDbContext db)
    {
        _db = db;
    }

    public Task<List<Company>> GetAllCompaniesAsync(CancellationToken cancellationToken)
        => _db.Companies.AsNoTracking().OrderBy(c => c.Name).ToListAsync(cancellationToken);

    public Task<Company?> GetCompanyByIdAsync(Guid companyId, CancellationToken cancellationToken)
        => _db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken);

    /// <summary>dbo.Users rows for a single company — read-only, no writes performed by this app.</summary>
    public Task<List<User>> GetCompanyUsersAsync(Guid companyId, CancellationToken cancellationToken)
        => _db.Users.AsNoTracking()
            .Where(u => u.CompanyId == companyId)
            .OrderBy(u => u.Email)
            .ToListAsync(cancellationToken);
}
