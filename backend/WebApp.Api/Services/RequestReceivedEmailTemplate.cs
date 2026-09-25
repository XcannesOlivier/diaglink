using System.Globalization;
using System.Net;
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
        var text = $"""
            {greeting}

            {subjectLine}

            Une autorisation de {amount} € a été enregistrée pour cette demande.
            Cette somme est actuellement réservée ; aucun débit n’a encore été effectué.

            Nous allons maintenant vérifier votre demande et préparer sa prise en charge.
            Vous recevrez un nouvel email lorsque votre demande aura été acceptée.

            L’équipe DiagLink
            """;
        var html = $"""
            <p>{WebUtility.HtmlEncode(greeting)}</p>
            <p>{WebUtility.HtmlEncode(subjectLine)}</p>
            <p>Une autorisation de <strong>{WebUtility.HtmlEncode(amount)} €</strong> a été enregistrée pour cette demande.<br>
            Cette somme est actuellement réservée ; aucun débit n’a encore été effectué.</p>
            <p>Nous allons maintenant vérifier votre demande et préparer sa prise en charge.<br>
            Vous recevrez un nouvel email lorsque votre demande aura été acceptée.</p>
            <p>L’équipe DiagLink</p>
            """;
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
        var nextLine = payload.RequestKind == MachineRequestKind.AdditionalDocuments
            ? "Nous allons maintenant préparer et intégrer ces nouveaux documents.\n\nVous recevrez un nouvel email lorsqu’ils seront disponibles dans DiagLink."
            : "Nous allons maintenant préparer votre machine et sa documentation.\n\nVous recevrez un nouvel email lorsque votre machine sera prête dans DiagLink.";
        var amount = Euros(payload.CapturedAmountCents);
        var text = $"{greeting}\n\n{requestLine}\n\nLe montant de {amount} € a été encaissé.\n\n{nextLine}\n\nL’équipe DiagLink";
        var html = $"<p>{Encode(greeting)}</p><p>{Encode(requestLine)}</p>"
            + $"<p>Le montant de <strong>{Encode(amount)} €</strong> a été encaissé.</p>"
            + $"<p>{Encode(nextLine).Replace("\n\n", "<br><br>", StringComparison.Ordinal)}</p><p>L’équipe DiagLink</p>";
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
        var text = $"{greeting}\n\n{requestLine}\n\nL’autorisation de paiement associée à cette demande a été annulée.\nAucun montant n’a été encaissé.\n\nL’équipe DiagLink";
        var html = $"<p>{Encode(greeting)}</p><p>{Encode(requestLine)}</p>"
            + "<p>L’autorisation de paiement associée à cette demande a été annulée.<br>Aucun montant n’a été encaissé.</p>"
            + "<p>L’équipe DiagLink</p>";
        return new("DiagLink — Mise à jour de votre demande", text, html);
    }

    private static TransactionalEmailContent BuildReady(string payloadJson, bool documents)
    {
        var payload = JsonSerializer.Deserialize<RequestReadyEmailPayload>(payloadJson, JsonOptions)
            ?? throw new InvalidDataException("The Ready payload is invalid.");
        if (documents != (payload.RequestKind == MachineRequestKind.AdditionalDocuments))
            throw new InvalidDataException("The Ready payload does not match its notification type.");
        var greeting = Greeting(payload.FirstName);
        var body = documents
            ? $"Les nouveaux documents ajoutés pour la machine \"{payload.MachineName}\" ont été intégrés.\n\nIls sont maintenant disponibles dans DiagLink et peuvent être utilisés par votre assistant technique."
            : $"La préparation de votre machine \"{payload.MachineName}\" est terminée.\n\nVotre machine et sa documentation sont maintenant disponibles dans DiagLink.\n\nVous pouvez vous connecter à DiagLink pour utiliser votre assistant technique.";
        var text = $"{greeting}\n\n{body}\n\nL’équipe DiagLink";
        var html = $"<p>{Encode(greeting)}</p><p>{Encode(body).Replace("\n\n", "</p><p>", StringComparison.Ordinal)}</p><p>L’équipe DiagLink</p>";
        return new(documents
            ? "DiagLink — Vos nouveaux documents sont disponibles"
            : "DiagLink — Votre machine est prête", text, html);
    }

    private static string Greeting(string? firstName) =>
        string.IsNullOrWhiteSpace(firstName) ? "Bonjour," : $"Bonjour {firstName.Trim()},";
    private static string Euros(long cents) =>
        (cents / 100m).ToString("N2", CultureInfo.GetCultureInfo("fr-FR"));
    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
