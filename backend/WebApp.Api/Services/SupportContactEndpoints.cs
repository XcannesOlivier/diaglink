using System.Net.Mail;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApp.Api.Data;
using WebApp.Api.Models;

namespace WebApp.Api.Services;

public static class SupportContactEndpoints
{
    public const string Route = "/api/contact";
    public const long MaxRequestBodyBytes = 16 * 1024;
    private const int MaxMessageLength = 5000;
    private const string DeliveryError = "Le message n’a pas pu être envoyé. Veuillez réessayer plus tard.";
    private const string MachineNotFoundError = "La machine demandée est introuvable.";

    public static void MapSupportContact(this WebApplication app) =>
        app.MapPost(Route, CreateAsync)
            .RequireAuthorization("SupportContact")
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBodyBytes))
            .WithName("SubmitSupportContact");

    public static async Task<IResult> CreateAsync(
        SupportContactRequest? request,
        HttpContext httpContext,
        IConfiguration configuration,
        DiagLinkDbContext db,
        DiagLinkUserLookupService userLookup,
        MachineAccessService machineAccess,
        ITransactionalEmailSender emailSender,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return Invalid("request", "La requête est obligatoire.");
        }

        var message = request.Message?.Trim();
        if (string.IsNullOrWhiteSpace(message))
        {
            return Invalid("message", "Le message est obligatoire.");
        }

        if (message.Length > MaxMessageLength || ContainsUnsupportedControlCharacter(message))
        {
            return Invalid("message", "Le message est invalide ou trop long.");
        }

        var configuredRecipient = configuration["Contact:RecipientAddress"]?.Trim();
        if (!TryParsePlainEmail(configuredRecipient, out var recipientEmail))
        {
            loggerFactory.CreateLogger("SupportContactEndpoints")
                .LogError("Contact recipient address is not configured or invalid.");
            return Results.Json(new { error = "Le service de contact est temporairement indisponible." },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (!Guid.TryParse(httpContext.User.FindFirst(DiagLinkClaimTypes.UserId)?.Value, out var userId))
        {
            return Results.Unauthorized();
        }

        var user = await userLookup.FindActiveUserByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var companyName = await db.Companies.AsNoTracking()
            .Where(company => company.Id == user.CompanyId && company.Status == "active")
            .Select(company => company.Name)
            .SingleOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(companyName))
        {
            return Results.Forbid();
        }

        string? machineDetails = null;
        if (request.MachineId is Guid machineId)
        {
            if (!await machineAccess.CanAccessMachineAsync(httpContext.User, machineId, cancellationToken))
            {
                return Results.NotFound(new { error = MachineNotFoundError });
            }

            var machine = await db.Machines.AsNoTracking()
                .Where(candidate => candidate.Id == machineId && candidate.CompanyId == user.CompanyId)
                .Select(candidate => new { candidate.Name, candidate.Reference })
                .SingleOrDefaultAsync(cancellationToken);
            if (machine is null)
            {
                return Results.NotFound(new { error = MachineNotFoundError });
            }

            machineDetails = string.IsNullOrWhiteSpace(machine.Reference)
                ? machine.Name
                : $"{machine.Name} ({machine.Reference})";
        }

        var displayName = string.Join(' ', new[] { user.FirstName, user.LastName }
            .Where(value => !string.IsNullOrWhiteSpace(value))) is { Length: > 0 } fullName
            ? fullName
            : user.Email;
        var body = $"""
            Nouveau message reçu depuis l’application DiagLink.

            Utilisateur : {displayName}
            E-mail : {user.Email}
            Entreprise : {companyName}
            Rôle : {user.Role}
            Machine : {machineDetails ?? "Non renseignée"}

            Message :
            {message}
            """;

        try
        {
            var operationId = await emailSender.SendAsync(
                recipientEmail!,
                "DiagLink — Message d’un utilisateur connecté",
                body,
                htmlBody: null,
                cancellationToken,
                replyToEmail: user.Email,
                replyToName: displayName);

            if (string.IsNullOrWhiteSpace(operationId))
            {
                loggerFactory.CreateLogger("SupportContactEndpoints")
                    .LogWarning("Support contact email provider did not confirm delivery.");
                return Results.Json(new { error = DeliveryError },
                    statusCode: StatusCodes.Status502BadGateway);
            }

            return Results.Ok(new ContactResponse(true));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            loggerFactory.CreateLogger("SupportContactEndpoints").LogWarning(
                "Support contact email delivery failed. ExceptionType={ExceptionType}", exception.GetType().Name);
            return Results.Json(new { error = DeliveryError },
                statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static IResult Invalid(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    private static bool TryParsePlainEmail(string? value, out string? email)
    {
        email = null;
        if (string.IsNullOrWhiteSpace(value) || !MailAddress.TryCreate(value, out var parsed) ||
            !string.Equals(parsed.Address, value, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        email = parsed.Address;
        return true;
    }

    private static bool ContainsUnsupportedControlCharacter(string value) =>
        value.Any(character => char.IsControl(character) && character is not '\r' and not '\n' and not '\t');
}