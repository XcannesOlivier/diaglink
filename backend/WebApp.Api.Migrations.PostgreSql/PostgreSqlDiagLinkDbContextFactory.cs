using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using WebApp.Api.Data;

namespace WebApp.Api.Migrations.PostgreSql;

/// <summary>
/// Design-time factory dedicated to the independent PostgreSQL migration history.
/// Creating the model does not open a database connection.
/// </summary>
public sealed class PostgreSqlDiagLinkDbContextFactory : IDesignTimeDbContextFactory<DiagLinkDbContext>
{
    public DiagLinkDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DiagLink")
            ?? "Host=localhost;Database=diaglink_design;Username=diaglink;Password=not-used";

        var options = new DbContextOptionsBuilder<DiagLinkDbContext>();
        options.UseNpgsql(connectionString, npgsql =>
        {
            npgsql.MigrationsAssembly(typeof(PostgreSqlDiagLinkDbContextFactory).Assembly.FullName);
            npgsql.MigrationsHistoryTable("__EFMigrationsHistory", DiagLinkDbContext.Schema);
            npgsql.SetPostgresVersion(17, 0);
        });

        return new DiagLinkDbContext(options.Options);
    }
}
