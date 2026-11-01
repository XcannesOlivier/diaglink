namespace WebApp.Api.Services;

public interface ITechnicalSkillProvider
{
    IReadOnlyList<TechnicalSkillDescriptor> AvailableSkills { get; }
    TechnicalSkillLoadResult Load(IReadOnlyList<string> skillNames, IReadOnlySet<string> alreadyLoaded);
}

public sealed record TechnicalSkillDescriptor(string Name, string Description);

public sealed record TechnicalSkillLoadResult(
    bool Success,
    string Code,
    string Message,
    string? Content = null,
    IReadOnlyList<string>? LoadedSkillNames = null);
