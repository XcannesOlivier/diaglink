using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.EventLog;

namespace WebApp.Api.Tests;

internal static class TestLoggingServices
{
    public static void RemoveWindowsEventLogProvider(this IServiceCollection services)
    {
        var eventLogDescriptors = services
            .Where(descriptor =>
                descriptor.ServiceType == typeof(ILoggerProvider) &&
                descriptor.ImplementationType == typeof(EventLogLoggerProvider))
            .ToArray();

        foreach (var descriptor in eventLogDescriptors)
        {
            services.Remove(descriptor);
        }
    }
}
