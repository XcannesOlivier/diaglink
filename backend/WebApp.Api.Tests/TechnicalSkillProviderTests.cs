using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

[TestClass]
public sealed class TechnicalSkillProviderTests
{
    private static readonly string[] ExpectedSkills =
    [
        "automate",
        "compte-rendu",
        "diagnostic",
        "documentation",
        "electrique",
        "escalade",
        "explications",
        "graissage",
        "hydraulique",
        "pieces",
        "pneumatique",
        "procedures",
        "schemas"
    ];

    private static readonly string[] ExpectedSkillFiles =
    [
        "Skills/automate/SKILL.md",
        "Skills/compte-rendu/SKILL.md",
        "Skills/diagnostic/SKILL.md",
        "Skills/documentation/SKILL.md",
        "Skills/electrique/SKILL.md",
        "Skills/escalade/SKILL.md",
        "Skills/explications/SKILL.md",
        "Skills/graissage/SKILL.md",
        "Skills/hydraulique/SKILL.md",
        "Skills/pieces/SKILL.md",
        "Skills/pneumatique/SKILL.md",
        "Skills/procedures/SKILL.md",
        "Skills/schemas/SKILL.md"
    ];

    private static TechnicalSkillProvider Provider() => new(
        AppContext.BaseDirectory,
        new ClaudeDirectSkillOptions { MaxSkillBytes = 32 * 1024 });

    [TestMethod]
    public void Load_ReturnsApprovedTextSkill()
    {
        var result = Provider().Load(["diagnostic"], new HashSet<string>());

        Assert.IsTrue(result.Success);
        Assert.AreEqual("loaded", result.Code);
        StringAssert.Contains(result.Content, "Mission");
        StringAssert.Contains(result.Content, "un seul contrôle");
    }

    [TestMethod]
    [DataRow("../diagnostic")]
    [DataRow("diagnostic/SKILL.md")]
    [DataRow("unknown")]
    [DataRow("")]
    public void Load_RejectsUnknownAndPathLikeIdentifiers(string value)
    {
        var result = Provider().Load([value], new HashSet<string>());

        Assert.IsFalse(result.Success);
        Assert.AreEqual("unknown_skill", result.Code);
    }

    [TestMethod]
    public void Load_IgnoresAlreadyLoadedSkillsAndLoadsRemainingSkills()
    {
        var result = Provider().Load(
            ["diagnostic", "pieces"],
            new HashSet<string> { "diagnostic" });

        Assert.IsTrue(result.Success);
        CollectionAssert.AreEqual(new[] { "pieces" }, result.LoadedSkillNames!.ToArray());
        StringAssert.Contains(result.Content, "===== SKILL: pieces =====");
        Assert.IsFalse(result.Content.Contains("===== SKILL: diagnostic =====", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Load_LoadsSeveralSkillsInOneResult()
    {
        var result = Provider().Load(
            ["diagnostic", "hydraulique", "procedures"],
            new HashSet<string>());

        Assert.IsTrue(result.Success);
        CollectionAssert.AreEqual(
            new[] { "diagnostic", "hydraulique", "procedures" },
            result.LoadedSkillNames!.ToArray());
        StringAssert.Contains(result.Content, "===== SKILL: diagnostic =====");
        StringAssert.Contains(result.Content, "===== SKILL: hydraulique =====");
        StringAssert.Contains(result.Content, "===== SKILL: procedures =====");
    }

    [TestMethod]
    public void Catalog_IsGeneratedFromTheThirteenSkillFrontmatters()
    {
        CollectionAssert.AreEqual(ExpectedSkills, Provider().AvailableSkills.Select(skill => skill.Name).ToArray());
    }

    [TestMethod]
    public void BuildOutput_ContainsBothPromptsAndExactlyThirteenApprovedSkills()
    {
        var promptsDirectory = Path.Combine(AppContext.BaseDirectory, "Prompts");

        Assert.IsTrue(File.Exists(Path.Combine(promptsDirectory, "TechnicalAssistantPrompt.claude-direct.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(promptsDirectory, "TechnicalAssistantPrompt.claude-direct.skills.txt")));

        var actualSkillFiles = Directory
            .EnumerateFiles(Path.Combine(promptsDirectory, "Skills"), "SKILL.md", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(promptsDirectory, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        var expectedSkillFiles = ExpectedSkillFiles.OrderBy(path => path, StringComparer.Ordinal).ToArray();

        CollectionAssert.AreEqual(expectedSkillFiles, actualSkillFiles);
    }
}
