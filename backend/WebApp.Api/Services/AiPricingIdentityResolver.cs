using WebApp.Api.Models;

namespace WebApp.Api.Services;

/// <summary>Exact, project-scoped tariff identity mapping captured before a provider call.</summary>
public sealed class AiPricingIdentityResolver(IConfiguration configuration, ILogger<AiPricingIdentityResolver> logger)
{
    public AiPricingIdentity Resolve(string projectEndpoint, string agentId, string agentVersion, string? modelDeployment = null)
    {
        var deployment = !string.IsNullOrWhiteSpace(modelDeployment) && modelDeployment.Length <= 200
            ? modelDeployment : null;
        if (deployment != null)
        {
            var deploymentMatches = configuration.GetSection("AiPricingIdentity:Deployments").GetChildren()
                .Where(entry => string.Equals(entry["ProjectEndpoint"], projectEndpoint, StringComparison.Ordinal)
                    && string.Equals(entry["Deployment"], deployment, StringComparison.Ordinal))
                .Take(2).ToArray();
            if (deploymentMatches.Length > 0)
            {
                var provider = deploymentMatches[0]["Provider"];
                if (deploymentMatches.Length == 1 && !string.IsNullOrWhiteSpace(provider) && provider.Length <= 100)
                    return new(provider, deployment);
                logger.LogWarning("Invalid or ambiguous pricing identity for project {ProjectEndpoint}, deployment {Deployment}",
                    projectEndpoint, deployment);
                return new(null, deployment);
            }
        }
        var matches = configuration.GetSection("AiPricingIdentity:Agents").GetChildren()
            .Where(entry => string.Equals(entry["ProjectEndpoint"], projectEndpoint, StringComparison.Ordinal)
                && string.Equals(entry["AgentId"], agentId, StringComparison.Ordinal)
                && string.Equals(entry["AgentVersion"], agentVersion, StringComparison.Ordinal))
            .Take(2).ToArray();
        if (matches.Length == 1)
        {
            var provider = matches[0]["Provider"];
            var mappedDeployment = matches[0]["Deployment"];
            if (!string.IsNullOrWhiteSpace(provider) && provider.Length <= 100
                && (mappedDeployment == null || (!string.IsNullOrWhiteSpace(mappedDeployment) && mappedDeployment.Length <= 200))
                && (deployment == null || mappedDeployment == null || string.Equals(deployment, mappedDeployment, StringComparison.Ordinal)))
                return new(provider, deployment ?? mappedDeployment);
        }
        logger.LogWarning("Pricing identity unavailable for project {ProjectEndpoint}, agent {AgentId}, version {AgentVersion}; mapping count {MappingCount}",
            projectEndpoint, agentId, agentVersion, matches.Length);
        return new(null, deployment);
    }
}

public sealed record AiPricingIdentity(string? Provider, string? Deployment)
{
    public AiResponseUsage ApplyTo(AiResponseUsage usage) =>
        usage with { Provider = Provider, Deployment = Deployment };
}
