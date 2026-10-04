using System.Text;

namespace WebApp.Api.Services;

public sealed class TechnicalAssistantPromptProvider : ITechnicalAssistantPromptProvider
{
    internal const string LegacyRelativePath = "Prompts/TechnicalAssistantPrompt.legacy.txt";
    internal const string ClaudeDirectRelativePath = "Prompts/TechnicalAssistantPrompt.claude-direct.txt";

    private readonly Lazy<string> _legacyPrompt;
    private readonly Lazy<string> _claudeDirectPrompt;

    public TechnicalAssistantPromptProvider(IHostEnvironment environment)
        : this(environment.ContentRootPath)
    {
    }

    internal TechnicalAssistantPromptProvider(string contentRootPath)
    {
        _legacyPrompt = CreatePromptLoader(contentRootPath, LegacyRelativePath);
        _claudeDirectPrompt = CreatePromptLoader(contentRootPath, ClaudeDirectRelativePath);
    }

    public string GetLegacyPrompt() => _legacyPrompt.Value;

    public string GetClaudeDirectPrompt() => _claudeDirectPrompt.Value;

    private static Lazy<string> CreatePromptLoader(string contentRootPath, string relativePath) =>
        new(
            () =>
            {
                var path = Path.GetFullPath(Path.Combine(
                    contentRootPath,
                    relativePath.Replace('/', Path.DirectorySeparatorChar)));
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException(
                        $"Technical assistant prompt resource is missing: {relativePath}",
                        path);
                }

                var prompt = File.ReadAllText(path, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(prompt))
                {
                    throw new InvalidDataException(
                        $"Technical assistant prompt resource is empty: {relativePath}");
                }

                return prompt;
            },
            LazyThreadSafetyMode.ExecutionAndPublication);
}
