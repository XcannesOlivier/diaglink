using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace WebApp.Api.Data;

/// <summary>
/// Configures the database provider without changing the DbContext or its model.
/// SQL Server remains the default so existing deployments keep their current behavior.
/// </summary>
internal static class DiagLinkDatabaseConfiguration
{
    internal const string PostgreSqlMigrationsAssembly = "WebApp.Api.Migrations.PostgreSql";
    internal const string ProviderConfigurationKey = "Database:Provider";
    internal const string ProviderEnvironmentVariable = "Database__Provider";
    internal const string SqlServerProvider = "SqlServer";
    internal const string PostgreSqlProvider = "PostgreSql";
    internal const int PostgreSqlMaximumPoolSize = 10;

    internal static void Configure(
        DbContextOptionsBuilder options,
        string connectionString,
        string? configuredProvider)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:DiagLink is not configured");
        }

        switch (NormalizeProvider(configuredProvider))
        {
            case SqlServerProvider:
                options.UseSqlServer(connectionString, sql =>
                {
                    sql.MigrationsHistoryTable("__EFMigrationsHistory", DiagLinkDbContext.Schema);
                    // Preserve Azure SQL transient-failure handling, including serverless cold starts.
                    sql.EnableRetryOnFailure(
                        maxRetryCount: 5,
                        maxRetryDelay: TimeSpan.FromSeconds(10),
                        errorNumbersToAdd: null);
                });
                break;

            case PostgreSqlProvider:
                var postgreSqlConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
                {
                    MaxPoolSize = PostgreSqlMaximumPoolSize
                }.ConnectionString;

                options.UseNpgsql(postgreSqlConnectionString, npgsql =>
                {
                    npgsql.MigrationsAssembly(PostgreSqlMigrationsAssembly);
                    npgsql.MigrationsHistoryTable("__EFMigrationsHistory", DiagLinkDbContext.Schema);
                    npgsql.SetPostgresVersion(17, 0);
                    npgsql.EnableRetryOnFailure(
                        maxRetryCount: 5,
                        maxRetryDelay: TimeSpan.FromSeconds(10),
                        errorCodesToAdd:
                        [
                            PostgresErrorCodes.SerializationFailure,
                            PostgresErrorCodes.DeadlockDetected
                        ]);
                });
                break;
        }
    }

    internal static string NormalizeProvider(string? configuredProvider)
    {
        if (string.IsNullOrWhiteSpace(configuredProvider)
            || configuredProvider.Equals(SqlServerProvider, StringComparison.OrdinalIgnoreCase))
        {
            return SqlServerProvider;
        }

        if (configuredProvider.Equals(PostgreSqlProvider, StringComparison.OrdinalIgnoreCase)
            || configuredProvider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase)
            || configuredProvider.Equals("Postgres", StringComparison.OrdinalIgnoreCase)
            || configuredProvider.Equals("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            return PostgreSqlProvider;
        }

        throw new InvalidOperationException(
            $"Unsupported database provider '{configuredProvider}'. " +
            $"Set {ProviderEnvironmentVariable} to '{SqlServerProvider}' or '{PostgreSqlProvider}'.");
    }

    internal static void EnsureProductionPostgreSqlTarget(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var isLoopback = string.Equals(builder.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(builder.Host, "localhost", StringComparison.OrdinalIgnoreCase);

        if (!string.Equals(builder.Database, "diaglink", StringComparison.Ordinal)
            || !isLoopback
            || builder.Port != 15432)
        {
            throw new InvalidOperationException(
                "Production PostgreSQL must target database 'diaglink' through the local SSH tunnel on port 15432.");
        }
    }
}
