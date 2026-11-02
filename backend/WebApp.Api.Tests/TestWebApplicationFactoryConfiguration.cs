using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using WebApp.Api.Data;

namespace WebApp.Api.Tests;

internal static class TestWebApplicationFactoryConfiguration
{
    private const string OfflinePostgreSqlConnectionString =
        "Host=127.0.0.1;Port=1;Database=diaglink_offline_tests;Username=offline";

    internal static IWebHostBuilder UseExplicitOfflinePostgreSqlBootstrapConfiguration(
        this IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DiagLinkDatabaseConfiguration.ProviderConfigurationKey] =
                    DiagLinkDatabaseConfiguration.PostgreSqlProvider,
                ["ConnectionStrings:DiagLink"] = OfflinePostgreSqlConnectionString
            }));
    }
}
