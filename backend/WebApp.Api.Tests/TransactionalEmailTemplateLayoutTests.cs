using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

[TestClass]
public sealed class TransactionalEmailTemplateLayoutTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public void EveryExistingMachineRequestTemplateUsesTheSharedAccessibleLayout()
    {
        foreach (var scenario in Scenarios())
        {
            var content = RequestReceivedEmailTemplate.Build(scenario.NotificationType,
                JsonSerializer.Serialize(scenario.Payload, scenario.Payload.GetType(), JsonOptions));

            Assert.AreEqual(scenario.ExpectedSubject, content.Subject, scenario.Name);
            StringAssert.Contains(content.TextBody, scenario.ExpectedBusinessText, scenario.Name);
            StringAssert.Contains(content.TextBody, $"Connexion : {DiagLinkTransactionalEmailLayout.LoginUrl}", scenario.Name);
            StringAssert.Contains(content.TextBody, "DiagLink · Assistant Technique", scenario.Name);

            StringAssert.Contains(content.HtmlBody,
                $"src=\"{DiagLinkTransactionalEmailLayout.LogoUrl}\"", scenario.Name);
            StringAssert.Contains(content.HtmlBody,
                $"href=\"{DiagLinkTransactionalEmailLayout.LoginUrl}\"", scenario.Name);
            StringAssert.Contains(content.HtmlBody, "Se connecter à DiagLink", scenario.Name);
            StringAssert.Contains(content.HtmlBody, "DiagLink · Assistant Technique", scenario.Name);
            StringAssert.Contains(content.HtmlBody, "&lt;Alice &amp; Bob&gt;", scenario.Name);
            StringAssert.Contains(content.HtmlBody, "Machine &lt;A &amp; B&gt;", scenario.Name);
            Assert.IsFalse(content.HtmlBody.Contains("<Alice & Bob>", StringComparison.Ordinal), scenario.Name);
            Assert.IsFalse(content.HtmlBody.Contains("Machine <A & B>", StringComparison.Ordinal), scenario.Name);
        }
    }

    private static IEnumerable<Scenario> Scenarios()
    {
        foreach (var kind in Enum.GetValues<MachineRequestKind>())
        {
            yield return new($"{kind}-received", EmailNotificationType.RequestReceived,
                Received(kind), RequestReceivedEmailTemplate.Subject, "autorisation");
            yield return new($"{kind}-accepted", EmailNotificationType.RequestAccepted,
                Accepted(kind), "DiagLink — Votre demande a été acceptée", "encaissé");
            yield return new($"{kind}-rejected", EmailNotificationType.RequestRejected,
                Rejected(kind), "DiagLink — Mise à jour de votre demande", "Aucun montant n’a été encaissé");
        }

        yield return new("initial-machine-ready", EmailNotificationType.MachineReady,
            Ready(MachineRequestKind.InitialMachine), "DiagLink — Votre machine est prête", "maintenant disponibles");
        yield return new("additional-machine-ready", EmailNotificationType.MachineReady,
            Ready(MachineRequestKind.AdditionalMachine), "DiagLink — Votre machine est prête", "maintenant disponibles");
        yield return new("documents-ready", EmailNotificationType.DocumentsReady,
            Ready(MachineRequestKind.AdditionalDocuments), "DiagLink — Vos nouveaux documents sont disponibles",
            "ont été intégrés");
    }

    private static RequestReceivedEmailPayload Received(MachineRequestKind kind) => new(kind, RequestId,
        "<Alice & Bob>", "client@example.test", "Machine <A & B>", 12980, "EUR");

    private static RequestAcceptedEmailPayload Accepted(MachineRequestKind kind) => new(kind, RequestId,
        "<Alice & Bob>", "client@example.test", "Machine <A & B>", 11403, "EUR");

    private static RequestRejectedEmailPayload Rejected(MachineRequestKind kind) => new(kind, RequestId,
        "<Alice & Bob>", "client@example.test", "Machine <A & B>", "EUR");

    private static RequestReadyEmailPayload Ready(MachineRequestKind kind) => new(kind, RequestId,
        "<Alice & Bob>", "client@example.test", "Machine <A & B>", DateTime.UtcNow);

    private static string RequestId { get; } = Guid.NewGuid().ToString("N");

    private sealed record Scenario(string Name, EmailNotificationType NotificationType, object Payload,
        string ExpectedSubject, string ExpectedBusinessText);
}
