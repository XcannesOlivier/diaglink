using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

[TestClass]
public sealed class CommercialPolicyProviderTests
{
    [TestMethod]
    public void AppSettings_ActivatesConfiguredCommercialPolicy()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();
        var options = configuration
            .GetSection(CommercialPolicyOptions.SectionName)
            .Get<CommercialPolicyOptions>();

        Assert.IsNotNull(options);
        Assert.IsTrue(options.Enabled);
        Assert.IsGreaterThanOrEqualTo(15, options.Instructions.Length);
        Assert.IsTrue(options.Instructions.Any(instruction =>
            instruction.Contains("compatibilité technique", StringComparison.Ordinal)));
        Assert.IsTrue(options.Instructions.Any(instruction =>
            instruction.Contains("France", StringComparison.Ordinal)));
        Assert.IsTrue(options.Instructions.Any(instruction =>
            instruction.Contains("trois options", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Options_AreLoadedFromCommercialPolicyConfigurationSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CommercialPolicy:Enabled"] = "true",
                ["CommercialPolicy:Instructions:0"] = "Instruction une",
                ["CommercialPolicy:Instructions:1"] = "Instruction deux"
            })
            .Build();
        var services = new ServiceCollection();
        services.Configure<CommercialPolicyOptions>(
            configuration.GetSection(CommercialPolicyOptions.SectionName));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<CommercialPolicyOptions>>().Value;

        Assert.IsTrue(options.Enabled);
        CollectionAssert.AreEqual(
            new[] { "Instruction une", "Instruction deux" },
            options.Instructions);
    }

    [TestMethod]
    public async Task ResolveAsync_EnabledPolicyReturnsConfiguredInstructions()
    {
        var provider = Provider(true, " Première instruction ", "Deuxième instruction");

        var snapshot = await provider.ResolveAsync(Guid.NewGuid());

        Assert.IsTrue(snapshot.Enabled);
        Assert.AreEqual("Première instruction\nDeuxième instruction", snapshot.Instructions);
    }

    [TestMethod]
    public async Task ResolveAsync_DisabledPolicyReturnsInactiveSnapshot()
    {
        var provider = Provider(false, "Instruction configurée");

        var snapshot = await provider.ResolveAsync(Guid.NewGuid());

        Assert.IsFalse(snapshot.Enabled);
        Assert.AreEqual(string.Empty, snapshot.Instructions);
    }

    [TestMethod]
    public async Task ResolveAsync_NoUsableInstructionsReturnsInactiveSnapshot()
    {
        var provider = Provider(true, string.Empty, "   ");

        var snapshot = await provider.ResolveAsync(Guid.NewGuid());

        Assert.IsFalse(snapshot.Enabled);
        Assert.AreEqual(string.Empty, snapshot.Instructions);
    }

    private static GlobalCommercialPolicyProvider Provider(
        bool enabled,
        params string[] instructions) => new(Options.Create(new CommercialPolicyOptions
        {
            Enabled = enabled,
            Instructions = instructions
        }));
}
