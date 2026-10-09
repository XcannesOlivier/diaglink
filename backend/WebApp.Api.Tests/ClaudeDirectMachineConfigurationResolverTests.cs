using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

[TestClass]
public sealed class ClaudeDirectMachineConfigurationResolverTests
{
    private const string ProjectEndpoint = "https://resource.test/api/projects/develon";
    private const string McpEndpoint =
        "https://resource.test/api/projects/develon/toolboxes/toolbox-dx10z/versions/1/mcp?api-version=v1";
    [TestMethod]
    public async Task ResolveAsync_ReadsValidToolboxMarker()
    {
        var fixture = Fixture.Valid();

        var result = await fixture.Resolver.ResolveAsync(Machine());

        Assert.AreEqual("toolbox-dx10z", result.ToolboxName);
        Assert.AreEqual("1", result.ToolboxVersion);
        Assert.AreEqual("vs_marker123", result.VectorStoreId);
        Assert.AreEqual(McpEndpoint, result.McpEndpoint);
        Assert.AreEqual("company/machine", result.BlobPrefix);
        Assert.AreEqual("company/machine/.foundry/toolbox.json", fixture.MarkerReader.RequestedBlobNames.Single());
    }

    [TestMethod]
    public async Task ResolveAsync_RejectsMissingMarker()
    {
        var fixture = Fixture.Valid();
        fixture.MarkerReader.Content = null;

        await AssertConfigurationError(fixture, Machine(), "toolbox_marker_missing");
    }

    [TestMethod]
    public async Task ResolveAsync_RejectsInvalidJson()
    {
        var fixture = Fixture.Valid();
        fixture.MarkerReader.Content = "{not-json";

        await AssertConfigurationError(fixture, Machine(), "toolbox_marker_invalid_json");
    }

    [TestMethod]
    public async Task ResolveAsync_RejectsMissingToolboxName()
    {
        var fixture = Fixture.WithMarker(toolboxName: null);

        await AssertConfigurationError(fixture, Machine(), "toolbox_name_invalid");
    }

    [TestMethod]
    public async Task ResolveAsync_RejectsMissingToolboxVersion()
    {
        var fixture = Fixture.WithMarker(toolboxVersion: null);

        await AssertConfigurationError(fixture, Machine(), "toolbox_version_invalid");
    }

    [TestMethod]
    public async Task ResolveAsync_RejectsInvalidVectorStoreId()
    {
        var fixture = Fixture.WithMarker(vectorStoreId: "store-without-prefix");

        await AssertConfigurationError(fixture, Machine(), "vector_store_id_invalid");
    }

    [TestMethod]
    public async Task ResolveAsync_RejectsHttpMcpEndpoint()
    {
        var fixture = Fixture.WithMarker(mcpEndpoint: McpEndpoint.Replace("https://", "http://", StringComparison.Ordinal));

        await AssertConfigurationError(fixture, Machine(), "mcp_endpoint_invalid");
    }

    [TestMethod]
    public async Task ResolveAsync_RejectsExternalMcpHostname()
    {
        var fixture = Fixture.WithMarker(mcpEndpoint: "https://attacker.test/mcp");

        await AssertConfigurationError(fixture, Machine(), "mcp_hostname_mismatch");
    }

    [TestMethod]
    public async Task ResolveAsync_RejectsMcpEndpointFromAnotherProject()
    {
        var fixture = Fixture.WithMarker(mcpEndpoint:
            "https://resource.test/api/projects/other/toolboxes/toolbox-dx10z/versions/1/mcp?api-version=v1");

        await AssertConfigurationError(fixture, Machine(), "mcp_project_mismatch");
    }

    [TestMethod]
    public async Task ResolveAsync_RejectsInvalidBlobPrefixBeforeMarkerRead()
    {
        var fixture = Fixture.Valid();
        var machine = Machine();
        machine.BlobPrefix = "company/../other-machine";

        await AssertConfigurationError(
            fixture,
            machine,
            "blob_prefix_invalid");
        Assert.HasCount(0, fixture.MarkerReader.RequestedBlobNames);
    }

    [TestMethod]
    public async Task ResolveAsync_RejectsMissingProjectEndpoint()
    {
        var fixture = Fixture.Valid();
        var machine = Machine();
        machine.ProjectEndpoint = null;

        await AssertConfigurationError(
            fixture,
            machine,
            "project_endpoint_missing");
    }

    [TestMethod]
    public void PromptProvider_ClaudeDirectContainsPrincipalBusinessRules()
    {
        var prompt = PromptProvider().GetClaudeDirectPrompt();

        foreach (var expected in new[]
        {
            "Utilise File Search comme outil documentaire principal pour les informations propres à la machine.",
            "Effectue normalement une première recherche ciblée.",
            "Effectue une recherche supplémentaire uniquement pour :",
            "Utilise get_page_image uniquement lorsqu’une information nécessaire dépend réellement du contenu visuel",
            "N’examine pas plusieurs zones lorsqu’une seule suffit.",
            "[Confirmé / Partiellement confirmé / Non confirmé]",
            "Partiellement confirmé — le trajet après RENVOI_Y reste à vérifier."
        })
        {
            StringAssert.Contains(prompt, expected);
        }
    }

    [TestMethod]
    public void PromptProvider_ClaudeDirectContainsIndicativeResponseLengthGuidance()
    {
        var prompt = PromptProvider().GetClaudeDirectPrompt();

        foreach (var expected in new[]
        {
            "40 à 100 mots",
            "150 mots",
            "50 à 120 mots",
            "30 à 80 mots",
            "120 mots",
            "60 à 140 mots",
            "200 mots",
            "80 à 250 mots",
            "350 mots",
            "80 mots",
            "150 à 300 mots"
        })
        {
            StringAssert.Contains(prompt, expected);
        }

        Assert.IsFalse(prompt.Contains(
            "jamais d’un nombre de mots prédéfini",
            StringComparison.Ordinal));
    }

    [TestMethod]
    public void PromptProvider_ClaudeDirectUsesStructuredSuggestionProtocol()
    {
        var prompt = PromptProvider().GetClaudeDirectPrompt();

        StringAssert.Contains(prompt, "<diaglink_suggestion>");
        StringAssert.Contains(prompt, "uniquement à la toute fin de la réponse");
        StringAssert.Contains(prompt, "jamais plus de deux balises");
        StringAssert.Contains(prompt, "Le caractère « ? » est une ponctuation normale");
        Assert.IsFalse(prompt.Contains(
            "L’interface actuelle peut transformer une phrase terminée par « ? »",
            StringComparison.Ordinal));
    }

    [TestMethod]
    public void RequestFactory_AcceptsNoClientMachineConfigurationObject()
    {
        var method = typeof(IClaudeDirectChatRequestFactory).GetMethod(nameof(IClaudeDirectChatRequestFactory.CreateAsync));
        var parameters = method!.GetParameters();

        CollectionAssert.AreEqual(
            new[] { typeof(Machine), typeof(IReadOnlyList<ClaudeDirectMessage>), typeof(CancellationToken) },
            parameters.Select(parameter => parameter.ParameterType).ToArray());
        Assert.IsFalse(parameters.Any(parameter => parameter.ParameterType == typeof(ClaudeDirectMachineContext)));
    }

    [TestMethod]
    public async Task ResolveAsync_AllowsEmptySqlVectorStoreId()
    {
        var fixture = Fixture.Valid();
        var machine = Machine();
        machine.VectorStoreId = null;

        var result = await fixture.Resolver.ResolveAsync(machine);

        Assert.AreEqual("vs_marker123", result.VectorStoreId);
        Assert.HasCount(0, result.Warnings);
    }

    [TestMethod]
    public async Task ResolveAsync_DetectsSqlAndMarkerVectorStoreMismatchWithoutLoggingIds()
    {
        var fixture = Fixture.Valid();
        var machine = Machine();
        machine.VectorStoreId = "vs_sqlDifferent";

        var result = await fixture.Resolver.ResolveAsync(machine);

        Assert.AreEqual("vector_store_mismatch", result.Warnings.Single().Code);
        Assert.IsTrue(fixture.Logger.Messages.Any(message => message.Contains("vector_store_mismatch", StringComparison.Ordinal)));
        Assert.IsFalse(fixture.Logger.Messages.Any(message =>
            message.Contains("vs_sqlDifferent", StringComparison.Ordinal) ||
            message.Contains("vs_marker123", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task RequestFactory_ProducesCompleteClaudeDirectChatRequest()
    {
        var fixture = Fixture.Valid();
        var policyProvider = new RecordingCommercialPolicyProvider();
        var factory = new ClaudeDirectChatRequestFactory(fixture.Resolver, policyProvider);
        var machine = Machine();
        machine.VectorStoreId = null;
        var messages = new[] { new ClaudeDirectMessage("user", "question") };

        var request = await factory.CreateAsync(machine, messages);

        Assert.AreEqual(ProjectEndpoint, request.Machine.ProjectEndpoint);
        Assert.AreEqual("toolbox-dx10z", request.Machine.ToolboxName);
        Assert.AreEqual("1", request.Machine.ToolboxVersion);
        Assert.AreEqual(McpEndpoint, request.Machine.McpEndpoint);
        Assert.AreEqual("vs_marker123", request.Machine.VectorStoreId);
        Assert.AreEqual("company/machine", request.Machine.BlobPrefix);
        Assert.AreEqual("PROMPT DIRECT", request.SystemPrompt);
        Assert.AreSame(messages, request.Messages);
        Assert.AreEqual(machine.CompanyId, policyProvider.ResolvedCompanyIds.Single());
        Assert.IsNotNull(request.CommercialPolicy);
        Assert.IsTrue(request.CommercialPolicy.Enabled);
        Assert.AreEqual("SERVER COMMERCIAL POLICY", request.CommercialPolicy.Instructions);
        Assert.AreNotEqual(request.SystemPrompt, request.Machine.Description);
        Assert.AreNotEqual(request.SystemPrompt, request.CommercialPolicy.Instructions);
        Assert.AreNotEqual(request.Machine.Description, request.CommercialPolicy.Instructions);
    }

    [TestMethod]
    public async Task RequestFactory_ResolvesEachMachinePolicyFromItsRealCompanyId()
    {
        var fixture = Fixture.Valid();
        var policyProvider = new RecordingCommercialPolicyProvider();
        var factory = new ClaudeDirectChatRequestFactory(fixture.Resolver, policyProvider);
        var firstMachine = Machine();
        var secondMachine = Machine();
        secondMachine.CompanyId = Guid.Parse("99999999-8888-7777-6666-555555555555");

        await factory.CreateAsync(firstMachine, []);
        await factory.CreateAsync(secondMachine, []);

        CollectionAssert.AreEqual(
            new[] { firstMachine.CompanyId, secondMachine.CompanyId },
            policyProvider.ResolvedCompanyIds);
    }

    [TestMethod]
    public void PromptProvider_FailsClearlyWhenResourceIsMissing()
    {
        var missingRoot = Path.Combine(Path.GetTempPath(), $"missing-prompts-{Guid.NewGuid():N}");
        var provider = new TechnicalAssistantPromptProvider(missingRoot);

        var exception = Assert.ThrowsExactly<FileNotFoundException>(() => provider.GetClaudeDirectPrompt());
        StringAssert.Contains(exception.Message, TechnicalAssistantPromptProvider.ClaudeDirectRelativePath);
    }

    private static async Task AssertConfigurationError(
        Fixture fixture,
        Machine machine,
        string expectedCode)
    {
        try
        {
            await fixture.Resolver.ResolveAsync(machine);
            Assert.Fail($"Expected configuration error {expectedCode}.");
        }
        catch (ClaudeDirectMachineConfigurationException exception)
        {
            Assert.AreEqual(expectedCode, exception.Code);
        }
    }

    private static Machine Machine() => new()
    {
        Id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
        CompanyId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        Name = "DX10z",
        Reference = "DX10z",
        Status = "active",
        ProjectEndpoint = ProjectEndpoint,
        BlobPrefix = "company/machine",
        VectorStoreId = "vs_marker123"
    };

    private static TechnicalAssistantPromptProvider PromptProvider() => new(AppContext.BaseDirectory);

    private sealed class Fixture
    {
        private Fixture(
            ClaudeDirectMachineConfigurationResolver resolver,
            FakeMarkerReader markerReader,
            CaptureLogger<ClaudeDirectMachineConfigurationResolver> logger)
        {
            Resolver = resolver;
            MarkerReader = markerReader;
            Logger = logger;
        }

        public ClaudeDirectMachineConfigurationResolver Resolver { get; }
        public FakeMarkerReader MarkerReader { get; }
        public CaptureLogger<ClaudeDirectMachineConfigurationResolver> Logger { get; }

        public static Fixture Valid() => WithMarker();

        public static Fixture WithMarker(
            string? toolboxName = "toolbox-dx10z",
            string? toolboxVersion = "1",
            string? vectorStoreId = "vs_marker123",
            string? mcpEndpoint = McpEndpoint)
        {
            var markerReader = new FakeMarkerReader
            {
                Content = JsonSerializer.Serialize(new Dictionary<string, string?>
                {
                    ["toolbox_name"] = toolboxName,
                    ["toolbox_version"] = toolboxVersion,
                    ["vector_store_id"] = vectorStoreId,
                    ["mcp_endpoint"] = mcpEndpoint
                })
            };
            var logger = new CaptureLogger<ClaudeDirectMachineConfigurationResolver>();
            var resolver = new ClaudeDirectMachineConfigurationResolver(
                markerReader,
                new StaticPromptProvider(),
                logger);
            return new(resolver, markerReader, logger);
        }
    }

    private sealed class FakeMarkerReader : IClaudeDirectToolboxMarkerReader
    {
        public string? Content { get; set; }
        public List<string> RequestedBlobNames { get; } = [];

        public Task<Stream?> OpenReadAsync(string blobName, CancellationToken cancellationToken)
        {
            RequestedBlobNames.Add(blobName);
            Stream? stream = Content is null
                ? null
                : new MemoryStream(Encoding.UTF8.GetBytes(Content));
            return Task.FromResult(stream);
        }
    }

    private sealed class StaticPromptProvider : ITechnicalAssistantPromptProvider
    {
        public string GetClaudeDirectPrompt() => "PROMPT DIRECT";
    }

    private sealed class RecordingCommercialPolicyProvider : ICommercialPolicyProvider
    {
        public List<Guid> ResolvedCompanyIds { get; } = [];

        public Task<CommercialPolicySnapshot> ResolveAsync(
            Guid companyId,
            CancellationToken cancellationToken = default)
        {
            ResolvedCompanyIds.Add(companyId);
            return Task.FromResult(new CommercialPolicySnapshot(true, "SERVER COMMERCIAL POLICY"));
        }
    }

    private sealed class CaptureLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
