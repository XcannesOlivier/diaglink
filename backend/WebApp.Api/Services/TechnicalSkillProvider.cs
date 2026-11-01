using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace WebApp.Api.Services;

public sealed class TechnicalSkillProvider : ITechnicalSkillProvider
{
    private static readonly Regex SkillNamePattern = new("\\A[a-z][a-z0-9-]{0,31}\\z", RegexOptions.CultureInvariant);

    private readonly IReadOnlyDictionary<string, SkillDefinition> _skills;
    private readonly TechnicalSkillDescriptor[] _availableSkills;
    private readonly ClaudeDirectSkillOptions _options;

    public TechnicalSkillProvider(IHostEnvironment environment, IOptions<ClaudeDirectChatOptions> options)
        : this(environment.ContentRootPath, options.Value.Skills)
    {
    }

    internal TechnicalSkillProvider(string contentRootPath, ClaudeDirectSkillOptions options)
    {
        var skillsRoot = Path.GetFullPath(Path.Combine(contentRootPath, "Prompts", "Skills"));
        _options = options;
        _skills = DiscoverSkills(skillsRoot);
        _availableSkills = _skills.Values
            .Select(skill => new TechnicalSkillDescriptor(skill.Name, skill.Description))
            .OrderBy(skill => skill.Name, StringComparer.Ordinal)
            .ToArray();
    }

    public IReadOnlyList<TechnicalSkillDescriptor> AvailableSkills => _availableSkills;

    public TechnicalSkillLoadResult Load(IReadOnlyList<string> skillNames, IReadOnlySet<string> alreadyLoaded)
    {
        if (skillNames is not { Count: > 0 })
            return new(false, "invalid_skill_request", "Au moins une compétence doit être demandée.");

        var requestedNames = skillNames.Distinct(StringComparer.Ordinal).ToArray();
        foreach (var skillName in requestedNames)
        {
            if (string.IsNullOrWhiteSpace(skillName) ||
                !SkillNamePattern.IsMatch(skillName) ||
                !_skills.ContainsKey(skillName))
            {
                return new(false, "unknown_skill", $"Compétence inconnue ou non autorisée : '{skillName}'.");
            }
        }

        var namesToLoad = requestedNames.Where(name => !alreadyLoaded.Contains(name)).ToArray();
        if (namesToLoad.Length == 0)
        {
            return new(
                true,
                "already_loaded",
                "Toutes les compétences demandées sont déjà disponibles dans ce tour.",
                "Aucune nouvelle compétence chargée : elles sont déjà disponibles dans ce tour.",
                []);
        }

        var contents = new List<(string Name, string Content)>(namesToLoad.Length);
        foreach (var skillName in namesToLoad)
        {
            var skill = _skills[skillName];
            var bytes = new FileInfo(skill.Path).Length;
            if (bytes is <= 0 || bytes > _options.MaxSkillBytes)
                return new(false, "skill_size_invalid", $"La compétence '{skillName}' est vide ou dépasse la taille autorisée.");

            var content = File.ReadAllText(skill.Path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(content))
                return new(false, "skill_size_invalid", $"La compétence '{skillName}' est vide.");
            contents.Add((skillName, content.Trim()));
        }

        var combined = string.Join(
            "\n\n",
            contents.Select(skill => $"===== SKILL: {skill.Name} =====\n{skill.Content}"));
        return new(
            true,
            "loaded",
            $"{namesToLoad.Length} compétence(s) chargée(s).",
            combined,
            namesToLoad);
    }

    private static IReadOnlyDictionary<string, SkillDefinition> DiscoverSkills(string skillsRoot)
    {
        if (!Directory.Exists(skillsRoot))
            throw new DirectoryNotFoundException($"Technical skills directory is missing: {skillsRoot}");

        var skills = new Dictionary<string, SkillDefinition>(StringComparer.Ordinal);
        foreach (var directory in Directory.EnumerateDirectories(skillsRoot).Order(StringComparer.Ordinal))
        {
            var path = Path.Combine(directory, "SKILL.md");
            if (!File.Exists(path))
                continue;

            var (name, description) = ReadFrontmatter(path);
            if (!SkillNamePattern.IsMatch(name))
                throw new InvalidDataException($"Invalid skill name '{name}' in {path}.");
            if (!skills.TryAdd(name, new(name, description, path)))
                throw new InvalidDataException($"Duplicate skill name '{name}' in {path}.");
        }

        if (skills.Count == 0)
            throw new InvalidDataException($"No technical skills were discovered in {skillsRoot}.");
        return skills;
    }

    private static (string Name, string Description) ReadFrontmatter(string path)
    {
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        if (!string.Equals(reader.ReadLine(), "---", StringComparison.Ordinal))
            throw new InvalidDataException($"Skill frontmatter is missing in {path}.");

        string? name = null;
        string? description = null;
        string? line;
        var closed = false;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.Equals(line, "---", StringComparison.Ordinal))
            {
                closed = true;
                break;
            }

            var separator = line.IndexOf(':');
            if (separator <= 0)
                continue;
            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (string.Equals(key, "name", StringComparison.Ordinal))
                name = value;
            else if (string.Equals(key, "description", StringComparison.Ordinal))
                description = value;
        }

        if (!closed || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(description))
            throw new InvalidDataException($"Skill frontmatter must define name and description in {path}.");
        return (name, description);
    }

    private sealed record SkillDefinition(string Name, string Description, string Path);
}
