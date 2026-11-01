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
    public void PromptProvider_ClaudeDirectContainsDiagnosticAndDocumentationInvariants()
    {
        var prompt = PromptProvider().GetClaudeDirectPrompt();

        foreach (var expected in new[]
        {
            "Parmi les contrôles sûrs, choisis celui qui départage le mieux les causes encore plausibles.",
            "Un résultat compatible avec une hypothèse ne la prouve pas.",
            "Dans ta réponse : un seul contrôle",
            "continuité et résistance se mesurent hors tension",
            "Une tension présente à vide ne prouve pas que le circuit tient en charge",
            "Pour un courant, utilise une pince ou une insertion en série",
            "Information propre à la machine : d'abord ce qui est déjà établi, puis File Search",
            "utilise get_page_image et affiche l'image documentaire pertinente",
            "Si la zone utile est connue, consulte directement le détail",
            "📄 Source : p. XX",
            "📄 Source : Page PDF : XX",
            "Ne la déduis jamais d'une figure",
            "Ne recopie pas de passages en anglais"
        })
        {
            StringAssert.Contains(prompt, expected);
        }
    }

    [TestMethod]
    public void PromptProvider_ClaudeDirectUsesTargetedMultilingualFileSearch()
    {
        var prompt = PromptProvider().GetClaudeDirectPrompt();

        StringAssert.Contains(prompt, "adapte les termes de recherche à la langue et à la terminologie technique de la documentation");
        StringAssert.Contains(prompt, "recherche directement dans cette langue, même si le technicien pose sa question en français");
        StringAssert.Contains(prompt, "Si les résultats sont insuffisants, reformule avec les équivalents techniques pertinents");
        StringAssert.Contains(prompt, "Privilégie les termes métier utilisés par les constructeurs plutôt qu'une traduction littérale.");
        StringAssert.Contains(prompt, "Ne lance pas systématiquement des recherches dans plusieurs langues.");
        StringAssert.Contains(prompt, "Conserve exactement les références constructeur, codes défaut, identifiants et désignations");
        StringAssert.Contains(prompt, "Avant de donner une mesure ou une intervention, vérifie les avertissements documentaires associés.");
        StringAssert.Contains(prompt, "Réponds en français.");
    }

    [TestMethod]
    public void PromptProvider_ClaudeDirectKeepsConciseResponseModesWithoutRigidTemplateCatalog()
    {
        var prompt = PromptProvider().GetClaudeDirectPrompt();

        StringAssert.Contains(prompt, "Court par défaut : quelques phrases.");
        StringAssert.Contains(prompt, "Développe seulement si le technicien demande une explication, une procédure complète ou un arbre de diagnostic");
        StringAssert.Contains(prompt, "sans jamais raccourcir au prix d'une précaution ou d'une donnée indispensable");
        StringAssert.Contains(prompt, "Dans ta réponse : un seul contrôle");
        StringAssert.Contains(prompt, "puis une seule question précise sur le résultat");
        StringAssert.Contains(prompt, "N'expose pas la liste des causes possibles, le fonctionnement complet du circuit ni les étapes suivantes.");
        StringAssert.Contains(prompt, "Ne raconte pas tes recherches et n'énumère pas les pistes écartées.");
        StringAssert.Contains(prompt, "Si le résultat est partiel, dis seulement ce qui reste à vérifier, avec au plus une action concrète.");
        Assert.IsFalse(prompt.Contains("Vise normalement 30 à 90 mots.", StringComparison.Ordinal));
        Assert.IsFalse(prompt.Contains("Maximum habituel :", StringComparison.Ordinal));
    }

    [TestMethod]
    public void PromptProvider_ClaudeDirectSeparatesVisualExplanationAndDiagramDiagnosticIntents()
    {
        var prompt = PromptProvider().GetClaudeDirectPrompt();

        StringAssert.Contains(prompt, "<images>");
        StringAssert.Contains(prompt, "Quand le technicien demande à voir un schéma, un plan, une implantation ou une vue éclatée");
        StringAssert.Contains(prompt, "Accompagne-la de très peu de texte");
        StringAssert.Contains(prompt, "les composants demandés avec leurs repères vérifiés");
        StringAssert.Contains(prompt, "N'explique pas le fonctionnement, le trajet, les bornes ou les connexions qui ne sont pas demandés.");
        StringAssert.Contains(prompt, "Pour une localisation, privilégie une photo ou illustration d'implantation");
        StringAssert.Contains(prompt, "Si la zone utile est connue, consulte directement le détail");
        StringAssert.Contains(prompt, "<diagnostic_guide>");
        StringAssert.Contains(prompt, "Dans ta réponse : un seul contrôle");
        StringAssert.Contains(prompt, "Développe seulement si le technicien demande une explication");
        StringAssert.Contains(prompt, "📄 Source : Page PDF : XX");
    }

    [TestMethod]
    public void PromptProvider_ClaudeDirectKeepsTechnicalPartStatusesAndExcludesCommercialSearch()
    {
        var prompt = PromptProvider().GetClaudeDirectPrompt();

        StringAssert.Contains(prompt, "1. Référence constructeur documentée");
        StringAssert.Contains(prompt, "2. Remplacement officiel confirmé");
        StringAssert.Contains(prompt, "3. Correspondance annoncée par une source tierce");
        StringAssert.Contains(prompt, "4. Alternative potentielle non confirmée");
        StringAssert.Contains(prompt, "5. Alternative compatible confirmée");
        StringAssert.Contains(prompt, "⚠️ Équivalence non confirmée — Ne pas commander sans vérification.");
        StringAssert.Contains(prompt, "Web : informations techniques uniquement. Aucune recherche de prix, stocks, vendeurs ou liens d'achat.");
        StringAssert.Contains(prompt, "Une page de fabricant ou de distributeur peut servir de source technique");
        Assert.IsFalse(prompt.Contains("# 8. RECHERCHE COMMERCIALE", StringComparison.Ordinal));
        Assert.IsFalse(prompt.Contains("Recherche et compare les prix", StringComparison.Ordinal));
        Assert.IsFalse(prompt.Contains("Politique commerciale active", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void PromptProvider_ClaudeDirectUsesStructuredSuggestionProtocol()
    {
        var prompt = PromptProvider().GetClaudeDirectPrompt();

        StringAssert.Contains(prompt, "<diaglink_suggestion>");
        StringAssert.Contains(prompt, "Les balises vont tout à la fin de la réponse");
        StringAssert.Contains(prompt, "Maximum 2 balises, et 1 en diagnostic guidé.");
        StringAssert.Contains(prompt, "Rédige chaque suggestion comme un message du technicien");
        StringAssert.Contains(prompt, "demande-le dans le corps de la réponse, pas dans une suggestion");
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
        var factory = new ClaudeDirectChatRequestFactory(fixture.Resolver);
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
        Assert.AreNotEqual(request.SystemPrompt, request.Machine.Description);
    }

    [TestMethod]
    public void PromptProvider_FailsClearlyWhenResourceIsMissing()
    {
        var missingRoot = Path.Combine(Path.GetTempPath(), $"missing-prompts-{Guid.NewGuid():N}");
        var provider = new TechnicalAssistantPromptProvider(missingRoot);

        var exception = Assert.ThrowsExactly<FileNotFoundException>(() => provider.GetClaudeDirectPrompt());
        StringAssert.Contains(exception.Message, TechnicalAssistantPromptProvider.ClaudeDirectRelativePath);
    }

    [TestMethod]
    public void PromptProvider_RequiresPagePrefixForEveryPageInAList()
    {
        var prompt = PromptProvider().GetClaudeDirectPrompt();

        StringAssert.Contains(prompt, "p. 17, p. 19, p. 32");
        StringAssert.Contains(prompt, "jamais « p. 17, 19, 32 »");
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
