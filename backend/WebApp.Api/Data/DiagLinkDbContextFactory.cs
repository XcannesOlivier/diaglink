using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace WebApp.Api.Data;

/// <summary>
/// Creates the application DbContext for EF Core design-time tooling without building the web host.
/// </summary>
public sealed class DiagLinkDbContextFactory : IDesignTimeDbContextFactory<DiagLinkDbContext>
{
    public DiagLinkDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DiagLink");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "A database connection is required for EF Core design-time operations. Set ConnectionStrings__DiagLink.");
        }

        var optionsBuilder = new DbContextOptionsBuilder<DiagLinkDbContext>();
        DiagLinkDatabaseConfiguration.Configure(
            optionsBuilder,
            connectionString,
            Environment.GetEnvironmentVariable(DiagLinkDatabaseConfiguration.ProviderEnvironmentVariable));

        return new DiagLinkDbContext(optionsBuilder.Options);
    }
}
