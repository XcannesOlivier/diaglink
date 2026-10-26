namespace WebApp.Api.Services;

public sealed class ClaudeDirectChatOptions
{
    public const string SectionName = "ClaudeDirectChat";

    public string FoundryAnthropicEndpoint { get; set; } = string.Empty;
    public string Deployment { get; set; } = "claude-sonnet-5-5";
    public int MaxMessageCalls { get; set; } = 8;
    public int MaxImageBytes { get; set; } = 5 * 1024 * 1024;
    public ClaudeDirectWebSearchOptions WebSearch { get; set; } = new();
    public ClaudeDirectFileSearchDiagnosticsOptions FileSearchDiagnostics { get; set; } = new();
}

public sealed class ClaudeDirectWebSearchOptions
{
    public bool Enabled { get; set; }
    public int MaxUses { get; set; } = 3;
}

public sealed class ClaudeDirectFileSearchDiagnosticsOptions
{
    public bool Enabled { get; set; }
}
