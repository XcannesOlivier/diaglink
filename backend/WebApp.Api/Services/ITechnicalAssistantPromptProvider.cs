namespace WebApp.Api.Services;

public interface ITechnicalAssistantPromptProvider
{
    string GetClaudeDirectPrompt();
    string GetClaudeDirectSkillsPrompt() => GetClaudeDirectPrompt();
}
