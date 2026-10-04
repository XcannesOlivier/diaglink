using System.Globalization;
using System.Text.Json;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;

namespace WebApp.Api.Services;

public sealed record TransactionalEmailContent(string Subject, string TextBody, string HtmlBody);

public static class RequestReceivedEmailTemplate
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public const string Subject = "DiagLink — Votre demande a bien été prise en compte";

    public static TransactionalEmailContent Build(EmailNotificationType notificationType, string payloadJson)
    {
        return notificationType switch
        {
            EmailNotificationType.RequestReceived => BuildReceived(payloadJson),
            EmailNotificationType.RequestAccepted => BuildAccepted(payloadJson),
            EmailNotificationType.RequestRejected => BuildRejected(payloadJson),
            EmailNotificationType.MachineReady => BuildReady(payloadJson, documents: false),
            EmailNotificationType.DocumentsReady => BuildReady(payloadJson, documents: true),
            _ => throw new NotSupportedException($"No email template is active for {notificationType}.")
        };
    }

    private static TransactionalEmailContent BuildReceived(string payloadJson)
    {
        var payload = JsonSerializer.Deserialize<RequestReceivedEmailPayload>(payloadJson, JsonOptions)
            ?? throw new InvalidDataException("The RequestReceived payload is invalid.");
        var greeting = string.IsNullOrWhiteSpace(payload.FirstName) ? "Bonjour," : $"Bonjour {payload.FirstName.Trim()},";
        var subjectLine = payload.RequestKind == MachineRequestKind.AdditionalDocuments
            ? $"Votre demande d’ajout de documents pour \"{payload.MachineName}\" a bien été prise en compte."
            : $"Votre demande concernant la machine \"{payload.MachineName}\" a bien été prise en compte.";
        var amount = (payload.AuthorizedAmountCents / 100m).ToString("N2", CultureInfo.GetCultureInfo("fr-FR"));
        string[] paragraphs =
        [
            subjectLine,
            "Une autorisation a été enregistrée pour cette demande.",
            "Cette somme est actuellement réservée ; aucun débit n’a encore été effectué.",
            "Nous allons maintenant vérifier votre demande et préparer sa prise en charge.",
            "Vous recevrez un nouvel email lorsque votre demande aura été acceptée."
        ];
        var highlight = $"Autorisation enregistrée : {amount} €";
        var text = DiagLinkTransactionalEmailLayout.BuildPlainText(greeting, paragraphs, highlight);
        var html = DiagLinkTransactionalEmailLayout.BuildHtml("Votre demande a bien été reçue", greeting,
            paragraphs, highlight);
        return new(Subject, text, html);
    }

    private static TransactionalEmailContent BuildAccepted(string payloadJson)
    {
        var payload = JsonSerializer.Deserialize<RequestAcceptedEmailPayload>(payloadJson, JsonOptions)
            ?? throw new InvalidDataException("The RequestAccepted payload is invalid.");
        var greeting = Greeting(payload.FirstName);
        var requestLine = payload.RequestKind == MachineRequestKind.AdditionalDocuments
            ? $"Votre demande d’ajout de documents pour la machine \"{payload.MachineName}\" a été acceptée."
            : $"Votre demande concernant la machine \"{payload.MachineName}\" a été acceptée.";
        string[] nextSteps = payload.RequestKind == MachineRequestKind.AdditionalDocuments
            ? ["Nous allons maintenant préparer et intégrer ces nouveaux documents.",
                "Vous recevrez un nouvel email lorsqu’ils seront disponibles dans DiagLink."]
            : ["Nous allons maintenant préparer votre machine et sa documentation.",
                "Vous recevrez un nouvel email lorsque votre machine sera prête dans DiagLink."];
        var amount = Euros(payload.CapturedAmountCents);
        var paragraphs = new[] { requestLine }.Concat(nextSteps).ToArray();
        var highlight = $"Montant encaissé : {amount} €";
        var text = DiagLinkTransactionalEmailLayout.BuildPlainText(greeting, paragraphs, highlight);
        var html = DiagLinkTransactionalEmailLayout.BuildHtml("Votre demande a été acceptée", greeting,
            paragraphs, highlight);
        return new("DiagLink — Votre demande a été acceptée", text, html);
    }

    private static TransactionalEmailContent BuildRejected(string payloadJson)
    {
        var payload = JsonSerializer.Deserialize<RequestRejectedEmailPayload>(payloadJson, JsonOptions)
            ?? throw new InvalidDataException("The RequestRejected payload is invalid.");
        var greeting = Greeting(payload.FirstName);
        var requestLine = payload.RequestKind == MachineRequestKind.AdditionalDocuments
            ? $"Votre demande d’ajout de documents pour la machine \"{payload.MachineName}\" n’a pas été acceptée."
            : $"Votre demande concernant la machine \"{payload.MachineName}\" n’a pas été acceptée.";
        string[] paragraphs =
        [
            requestLine,
            "L’autorisation de paiement associée à cette demande a été annulée.",
            "Aucun montant n’a été encaissé."
        ];
        var text = DiagLinkTransactionalEmailLayout.BuildPlainText(greeting, paragraphs,
            "Autorisation de paiement annulée");
        var html = DiagLinkTransactionalEmailLayout.BuildHtml("Mise à jour de votre demande", greeting,
            paragraphs, "Autorisation de paiement annulée");
        return new("DiagLink — Mise à jour de votre demande", text, html);
    }

    private static TransactionalEmailContent BuildReady(string payloadJson, bool documents)
    {
        var payload = JsonSerializer.Deserialize<RequestReadyEmailPayload>(payloadJson, JsonOptions)
            ?? throw new InvalidDataException("The Ready payload is invalid.");
        if (documents != (payload.RequestKind == MachineRequestKind.AdditionalDocuments))
            throw new InvalidDataException("The Ready payload does not match its notification type.");
        var greeting = Greeting(payload.FirstName);
        string[] paragraphs = documents
            ? [$"Les nouveaux documents ajoutés pour la machine \"{payload.MachineName}\" ont été intégrés.",
                "Ils sont maintenant disponibles dans DiagLink et peuvent être utilisés par votre assistant technique."]
            : [$"La préparation de votre machine \"{payload.MachineName}\" est terminée.",
                "Votre machine et sa documentation sont maintenant disponibles dans DiagLink.",
                "Vous pouvez vous connecter à DiagLink pour utiliser votre assistant technique."];
        var title = documents ? "Vos nouveaux documents sont disponibles" : "Votre machine est prête";
        var highlight = documents ? "Documents disponibles" : "Machine prête";
        var text = DiagLinkTransactionalEmailLayout.BuildPlainText(greeting, paragraphs, highlight);
        var html = DiagLinkTransactionalEmailLayout.BuildHtml(title, greeting, paragraphs, highlight);
        return new(documents
            ? "DiagLink — Vos nouveaux documents sont disponibles"
            : "DiagLink — Votre machine est prête", text, html);
    }

    private static string Greeting(string? firstName) =>
        string.IsNullOrWhiteSpace(firstName) ? "Bonjour," : $"Bonjour {firstName.Trim()},";
    private static string Euros(long cents) =>
        (cents / 100m).ToString("N2", CultureInfo.GetCultureInfo("fr-FR"));
}
