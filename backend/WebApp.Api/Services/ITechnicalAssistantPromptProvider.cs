namespace WebApp.Api.Services;

public interface ITechnicalAssistantPromptProvider
{
    string GetLegacyPrompt();
    string GetClaudeDirectPrompt();
}
