namespace WebApp.Api.Models;

public sealed record ClaudeDirectConfigurationWarning(string Code, string Message);

public sealed record ClaudeDirectMachineConfiguration(
    string ProjectEndpoint,
    string ToolboxName,
    string ToolboxVersion,
    string McpEndpoint,
    string VectorStoreId,
    string BlobPrefix,
    string SystemPrompt,
    string Description,
    IReadOnlyList<ClaudeDirectConfigurationWarning> Warnings);

public sealed class ClaudeDirectMachineConfigurationException : InvalidOperationException
{
    public ClaudeDirectMachineConfigurationException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}
