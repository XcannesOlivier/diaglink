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
        var logoContent = EmailService.LoadEmbeddedLogo();
        Assert.IsNotNull(logoContent);
        var notificationTypes = new HashSet<EmailNotificationType>();

        foreach (var scenario in Scenarios())
        {
            notificationTypes.Add(scenario.NotificationType);
            var content = RequestReceivedEmailTemplate.Build(scenario.NotificationType,
                JsonSerializer.Serialize(scenario.Payload, scenario.Payload.GetType(), JsonOptions));

            Assert.AreEqual(scenario.ExpectedSubject, content.Subject, scenario.Name);
            StringAssert.Contains(content.TextBody, scenario.ExpectedBusinessText, scenario.Name);
            StringAssert.Contains(content.TextBody, $"Connexion : {DiagLinkTransactionalEmailLayout.LoginUrl}", scenario.Name);
            StringAssert.Contains(content.TextBody, "DiagLink · Assistant Technique", scenario.Name);

            StringAssert.Contains(content.HtmlBody, "src=\"cid:diaglink-logo\"", scenario.Name);
            Assert.IsFalse(content.HtmlBody.Contains("https://diaglink.com/assets/", StringComparison.Ordinal),
                scenario.Name);
            StringAssert.Contains(content.HtmlBody,
                $"href=\"{DiagLinkTransactionalEmailLayout.LoginUrl}\"", scenario.Name);
            StringAssert.Contains(content.HtmlBody, "Se connecter à DiagLink", scenario.Name);
            StringAssert.Contains(content.HtmlBody, "DiagLink · Assistant Technique", scenario.Name);
            StringAssert.Contains(content.HtmlBody, "&lt;Alice &amp; Bob&gt;", scenario.Name);
            StringAssert.Contains(content.HtmlBody, "Machine &lt;A &amp; B&gt;", scenario.Name);
            Assert.IsFalse(content.HtmlBody.Contains("<Alice & Bob>", StringComparison.Ordinal), scenario.Name);
            Assert.IsFalse(content.HtmlBody.Contains("Machine <A & B>", StringComparison.Ordinal), scenario.Name);

            var message = EmailService.BuildTransactionalMessage(
                "connexion@diaglink.com",
                "client@example.test",
                content.Subject,
                content.TextBody,
                content.HtmlBody,
                logoContent);

            Assert.AreEqual(1, message.Attachments.Count, scenario.Name);
            var attachment = message.Attachments.Single();
            Assert.AreEqual("diaglink-logo.png", attachment.Name, scenario.Name);
            Assert.AreEqual("image/png", attachment.ContentType, scenario.Name);
            Assert.AreEqual("diaglink-logo", attachment.ContentId, scenario.Name);
        }

        CollectionAssert.AreEquivalent(Enum.GetValues<EmailNotificationType>(), notificationTypes.ToArray());
    }

    [TestMethod]
    public void PlainTextTransactionalEmailKeepsReplyToAndHasNoInlineAttachment()
    {
        var message = EmailService.BuildTransactionalMessage(
            "connexion@diaglink.com",
            "contact@diaglink.com",
            "Sujet inchangé",
            "Corps texte inchangé",
            htmlBody: null,
            EmailService.LoadEmbeddedLogo(),
            replyToEmail: "client@example.test",
            replyToName: "Client DiagLink");

        Assert.AreEqual(0, message.Attachments.Count);
        Assert.AreEqual("Sujet inchangé", message.Content.Subject);
        Assert.AreEqual("Corps texte inchangé", message.Content.PlainText);
        Assert.AreEqual(1, message.ReplyTo.Count);
        Assert.AreEqual("client@example.test", message.ReplyTo.Single().Address);
        Assert.AreEqual("Client DiagLink", message.ReplyTo.Single().DisplayName);
    }

    [TestMethod]
    public void HtmlWithoutLogoCidHasNoInlineAttachment()
    {
        var message = EmailService.BuildTransactionalMessage(
            "connexion@diaglink.com",
            "client@example.test",
            "Sujet inchangé",
            "Corps texte inchangé",
            "<p>Corps HTML inchangé</p>",
            EmailService.LoadEmbeddedLogo());

        Assert.AreEqual(0, message.Attachments.Count);
        Assert.AreEqual("<p>Corps HTML inchangé</p>", message.Content.Html);
    }

    [TestMethod]
    public void MissingLogoStillBuildsCompleteHtmlTransactionalEmail()
    {
        var scenario = Scenarios().Single(item =>
            item.NotificationType == EmailNotificationType.DocumentsReady);
        var content = RequestReceivedEmailTemplate.Build(scenario.NotificationType,
            JsonSerializer.Serialize(scenario.Payload, scenario.Payload.GetType(), JsonOptions));

        var message = EmailService.BuildTransactionalMessage(
            "connexion@diaglink.com",
            "client@example.test",
            content.Subject,
            content.TextBody,
            content.HtmlBody,
            logoContent: null);

        Assert.AreEqual(0, message.Attachments.Count);
        Assert.AreEqual(content.Subject, message.Content.Subject);
        Assert.AreEqual(content.TextBody, message.Content.PlainText);
        Assert.AreEqual(content.HtmlBody, message.Content.Html);
        StringAssert.Contains(message.Content.Html, "src=\"cid:diaglink-logo\"");
        StringAssert.Contains(message.Content.Html, "alt=\"DiagLink\"");
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
