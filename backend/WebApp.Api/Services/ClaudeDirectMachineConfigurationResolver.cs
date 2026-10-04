using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;

namespace WebApp.Api.Services;

public sealed class ClaudeDirectMachineConfigurationResolver(
    IClaudeDirectToolboxMarkerReader markerReader,
    ITechnicalAssistantPromptProvider promptProvider,
    ILogger<ClaudeDirectMachineConfigurationResolver> logger)
    : IClaudeDirectMachineConfigurationResolver
{
    private const int MaxMarkerBytes = 64 * 1024;
    private static readonly Regex BlobSegmentPattern = new(
        @"\A[a-z0-9]+(?:-[a-z0-9]+)*\z",
        RegexOptions.CultureInvariant);
    private static readonly Regex ToolboxPartPattern = new(
        @"\A[A-Za-z0-9][A-Za-z0-9._-]{0,127}\z",
        RegexOptions.CultureInvariant);
    private static readonly Regex VectorStorePattern = new(
        @"\Avs_[A-Za-z0-9]+\z",
        RegexOptions.CultureInvariant);

    public async Task<ClaudeDirectMachineConfiguration> ResolveAsync(
        Machine machine,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(machine);

        if (!TryProjectEndpoint(machine.ProjectEndpoint, out var projectEndpoint))
        {
            throw ConfigurationError("project_endpoint_missing", "Machine.ProjectEndpoint is missing or invalid.");
        }

        if (!TryBlobPrefix(machine.BlobPrefix, out var blobPrefix))
        {
            throw ConfigurationError("blob_prefix_invalid", "Machine.BlobPrefix is missing or invalid.");
        }

        var markerBlobName = $"{blobPrefix}/.foundry/toolbox.json";
        await using var markerStream = await markerReader.OpenReadAsync(markerBlobName, cancellationToken);
        if (markerStream is null)
        {
            throw ConfigurationError("toolbox_marker_missing", "The machine Toolbox marker was not found.");
        }

        ToolboxMarker marker;
        try
        {
            var markerBytes = await ReadBoundedAsync(markerStream, MaxMarkerBytes, cancellationToken);
            marker = JsonSerializer.Deserialize<ToolboxMarker>(markerBytes)
                ?? throw new JsonException("Marker deserialized to null.");
        }
        catch (JsonException exception)
        {
            throw new ClaudeDirectMachineConfigurationException(
                "toolbox_marker_invalid_json",
                "The machine Toolbox marker contains invalid JSON.",
                exception);
        }

        if (string.IsNullOrWhiteSpace(marker.ToolboxName) || !ToolboxPartPattern.IsMatch(marker.ToolboxName))
        {
            throw ConfigurationError("toolbox_name_invalid", "The Toolbox marker has no valid toolbox_name.");
        }

        if (string.IsNullOrWhiteSpace(marker.ToolboxVersion) || !ToolboxPartPattern.IsMatch(marker.ToolboxVersion))
        {
            throw ConfigurationError("toolbox_version_invalid", "The Toolbox marker has no valid toolbox_version.");
        }

        if (string.IsNullOrWhiteSpace(marker.VectorStoreId) || !VectorStorePattern.IsMatch(marker.VectorStoreId))
        {
            throw ConfigurationError("vector_store_id_invalid", "The Toolbox marker has no valid vector_store_id.");
        }

        if (!Uri.TryCreate(marker.McpEndpoint, UriKind.Absolute, out var mcpEndpoint) ||
            mcpEndpoint.Scheme != Uri.UriSchemeHttps)
        {
            throw ConfigurationError("mcp_endpoint_invalid", "The Toolbox marker MCP endpoint must be HTTPS.");
        }

        if (!string.Equals(mcpEndpoint.Host, projectEndpoint.Host, StringComparison.OrdinalIgnoreCase))
        {
            throw ConfigurationError("mcp_hostname_mismatch", "The Toolbox marker MCP hostname does not match Machine.ProjectEndpoint.");
        }

        var expectedMcpEndpoint = BuildExpectedMcpEndpoint(
            projectEndpoint,
            marker.ToolboxName,
            marker.ToolboxVersion);
        if (Uri.Compare(
                expectedMcpEndpoint,
                mcpEndpoint,
                UriComponents.HttpRequestUrl,
                UriFormat.Unescaped,
                StringComparison.OrdinalIgnoreCase) != 0)
        {
            throw ConfigurationError("mcp_project_mismatch", "The Toolbox marker MCP endpoint does not match the machine project and Toolbox.");
        }

        var warnings = new List<ClaudeDirectConfigurationWarning>();
        if (!string.IsNullOrWhiteSpace(machine.VectorStoreId) &&
            !string.Equals(machine.VectorStoreId, marker.VectorStoreId, StringComparison.Ordinal))
        {
            logger.LogWarning(
                "Claude direct machine configuration has a Vector Store mismatch. MachineId={MachineId}, Code={Code}",
                machine.Id,
                "vector_store_mismatch");
            warnings.Add(new(
                "vector_store_mismatch",
                "Machine.VectorStoreId differs from the Toolbox marker; the marker remains authoritative."));
        }

        var description = string.IsNullOrWhiteSpace(machine.Reference)
            ? $"Machine : {machine.Name}."
            : $"Machine : {machine.Name}. Référence : {machine.Reference}.";

        return new(
            projectEndpoint.AbsoluteUri.TrimEnd('/'),
            marker.ToolboxName,
            marker.ToolboxVersion,
            mcpEndpoint.AbsoluteUri,
            marker.VectorStoreId,
            blobPrefix,
            promptProvider.GetClaudeDirectPrompt(),
            description,
            warnings);
    }

    private static bool TryProjectEndpoint(string? value, out Uri endpoint)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out endpoint!) ||
            endpoint.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(endpoint.Query) ||
            !string.IsNullOrEmpty(endpoint.Fragment))
        {
            return false;
        }

        var segments = endpoint.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 3 &&
               string.Equals(segments[0], "api", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(segments[1], "projects", StringComparison.OrdinalIgnoreCase) &&
               ToolboxPartPattern.IsMatch(Uri.UnescapeDataString(segments[2]));
    }

    private static bool TryBlobPrefix(string? value, out string blobPrefix)
    {
        blobPrefix = value?.Trim('/') ?? string.Empty;
        var segments = blobPrefix.Split('/');
        return value == blobPrefix &&
               segments.Length == 2 &&
               segments.All(segment => BlobSegmentPattern.IsMatch(segment));
    }

    private static Uri BuildExpectedMcpEndpoint(Uri projectEndpoint, string toolboxName, string toolboxVersion)
    {
        var projectBase = new Uri(projectEndpoint.AbsoluteUri.TrimEnd('/') + "/");
        return new Uri(
            projectBase,
            $"toolboxes/{Uri.EscapeDataString(toolboxName)}/versions/{Uri.EscapeDataString(toolboxVersion)}/mcp?api-version=v1");
    }

    private static async Task<byte[]> ReadBoundedAsync(
        Stream stream,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0)
            {
                return output.ToArray();
            }

            if (output.Length + read > maxBytes)
            {
                throw ConfigurationError("toolbox_marker_too_large", "The machine Toolbox marker is too large.");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static ClaudeDirectMachineConfigurationException ConfigurationError(string code, string message) =>
        new(code, message);

    private sealed record ToolboxMarker(
        [property: JsonPropertyName("toolbox_name")] string? ToolboxName,
        [property: JsonPropertyName("toolbox_version")] string? ToolboxVersion,
        [property: JsonPropertyName("vector_store_id")] string? VectorStoreId,
        [property: JsonPropertyName("mcp_endpoint")] string? McpEndpoint);
}
