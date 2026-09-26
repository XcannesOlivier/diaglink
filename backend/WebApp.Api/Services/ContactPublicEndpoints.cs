using System.Net;
using System.Net.Mail;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using WebApp.Api.Models;

namespace WebApp.Api.Services;

public static partial class ContactPublicEndpoints
{
    public const string Route = "/api/public/contact";
    public const long MaxRequestBodyBytes = 16 * 1024;
    private const int MaxNameLength = 120;
    private const int MaxCompanyLength = 200;
    private const int MaxEmailLength = 254;
    private const int MaxPhoneLength = 30;
    private const int MaxMessageLength = 5000;
    private const string DeliveryError = "Le message n’a pas pu être envoyé. Veuillez réessayer plus tard.";

    public static void MapPublicContact(this WebApplication app) =>
        app.MapPost(Route, CreateAsync)
            .AllowAnonymous()
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBodyBytes))
            .WithName("SubmitPublicContact");

    public static async Task<IResult> CreateAsync(
        ContactRequest? request,
        HttpContext httpContext,
        IConfiguration configuration,
        ITransactionalEmailSender emailSender,
        ContactSubmissionRateLimiter rateLimiter,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var configuredRecipient = configuration["Contact:RecipientAddress"]?.Trim();
        if (!TryParsePlainEmail(configuredRecipient, out var recipientEmail))
        {
            loggerFactory.CreateLogger("ContactPublicEndpoints")
                .LogError("Contact recipient address is not configured or invalid.");
            return Results.Json(new { error = "Le service de contact est temporairement indisponible." },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var ipRateLimit = rateLimiter.AttemptIp(ClientIpAddress(httpContext));
        if (!ipRateLimit.IsAllowed) return RateLimitExceeded(httpContext, ipRateLimit.RetryAfter);

        if (request is null) return Invalid("request", "La requête est obligatoire.").Item2;
        var validationResult = Validate(request);
        if (validationResult.Error is not null) return validationResult.Error;
        var contact = validationResult.Request!;

        var emailRateLimit = rateLimiter.AttemptEmail(contact.Email!);
        if (!emailRateLimit.IsAllowed) return RateLimitExceeded(httpContext, emailRateLimit.RetryAfter);

        var body = $"""
            Nouveau message reçu depuis le formulaire public DiagLink.

            Nom : {contact.Name}
            Entreprise : {contact.Company ?? "Non renseignée"}
            E-mail : {contact.Email}
            Téléphone : {contact.Phone ?? "Non renseigné"}

            Message :
            {contact.Message}
            """;

        try
        {
            var operationId = await emailSender.SendAsync(
                recipientEmail!,
                "DiagLink — Nouveau message reçu via le formulaire de contact",
                body,
                htmlBody: null,
                cancellationToken,
                replyToEmail: contact.Email,
                replyToName: contact.Name);

            if (string.IsNullOrWhiteSpace(operationId))
            {
                loggerFactory.CreateLogger("ContactPublicEndpoints")
                    .LogWarning("Contact email provider did not confirm delivery.");
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
            loggerFactory.CreateLogger("ContactPublicEndpoints").LogWarning(
                "Contact email delivery failed. ExceptionType={ExceptionType}", exception.GetType().Name);
            return Results.Json(new { error = DeliveryError },
                statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static (ContactRequest? Request, IResult? Error) Validate(ContactRequest request)
    {
        var normalized = new ContactRequest(
            request.Name?.Trim(),
            NullIfEmpty(request.Company?.Trim()),
            request.Email?.Trim(),
            NullIfEmpty(request.Phone?.Trim()),
            request.Message?.Trim());

        if (string.IsNullOrWhiteSpace(normalized.Name)) return Invalid("name", "Le nom est obligatoire.");
        if (normalized.Name.Length > MaxNameLength || ContainsLineBreak(normalized.Name))
            return Invalid("name", "Le nom est invalide ou trop long.");
        if (normalized.Company?.Length > MaxCompanyLength || ContainsLineBreak(normalized.Company))
            return Invalid("company", "L’entreprise est invalide ou trop longue.");
        if (string.IsNullOrWhiteSpace(normalized.Email)) return Invalid("email", "L’adresse e-mail est obligatoire.");
        if (normalized.Email.Length > MaxEmailLength || !TryParsePlainEmail(normalized.Email, out var email))
            return Invalid("email", "L’adresse e-mail est invalide.");
        if (normalized.Phone?.Length > MaxPhoneLength ||
            (normalized.Phone is not null && !PhonePattern().IsMatch(normalized.Phone)))
            return Invalid("phone", "Le numéro de téléphone est invalide.");
        if (string.IsNullOrWhiteSpace(normalized.Message)) return Invalid("message", "Le message est obligatoire.");
        if (normalized.Message.Length > MaxMessageLength || ContainsUnsupportedControlCharacter(normalized.Message))
            return Invalid("message", "Le message est invalide ou trop long.");

        return (normalized with { Email = email }, null);
    }

    private static (ContactRequest?, IResult) Invalid(string field, string message) =>
        (null, Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] }));

    private static IResult RateLimitExceeded(HttpContext httpContext, TimeSpan? retryAfter)
    {
        var retryAfterSeconds = Math.Max(1,
            (int)Math.Ceiling((retryAfter ?? ContactSubmissionRateLimiter.Window).TotalSeconds));
        httpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        return Results.Json(new { error = "Trop de messages ont été envoyés. Veuillez réessayer plus tard." },
            statusCode: StatusCodes.Status429TooManyRequests);
    }

    private static bool TryParsePlainEmail(string? value, out string? email)
    {
        email = null;
        if (string.IsNullOrWhiteSpace(value) || !MailAddress.TryCreate(value, out var parsed) ||
            !string.Equals(parsed.Address, value, StringComparison.OrdinalIgnoreCase)) return false;
        email = parsed.Address;
        return true;
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
    private static bool ContainsLineBreak(string? value) => value?.IndexOfAny(['\r', '\n']) >= 0;
    private static bool ContainsUnsupportedControlCharacter(string value) =>
        value.Any(character => char.IsControl(character) && character is not '\r' and not '\n' and not '\t');

    private static IPAddress? ClientIpAddress(HttpContext httpContext)
    {
        var forwardedFor = httpContext.Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrWhiteSpace(forwardedFor))
        {
            var values = forwardedFor.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            for (var index = values.Length - 1; index >= 0; index--)
                if (IPAddress.TryParse(values[index], out var address)) return address;
        }
        return httpContext.Connection.RemoteIpAddress;
    }

    [GeneratedRegex(@"^\+?[\d\s().-]{7,30}$", RegexOptions.CultureInvariant)]
    private static partial Regex PhonePattern();
}

public sealed class ContactSubmissionRateLimiter : IDisposable
{
    public const int EmailPermitLimit = 3;
    public const int IpPermitLimit = 20;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private readonly PartitionedRateLimiter<string> byEmail = CreateLimiter(EmailPermitLimit);
    private readonly PartitionedRateLimiter<string> byIp = CreateLimiter(IpPermitLimit);

    public (bool IsAllowed, TimeSpan? RetryAfter) AttemptIp(IPAddress? remoteIpAddress) =>
        Attempt(byIp, remoteIpAddress?.ToString() ?? "unknown");

    public (bool IsAllowed, TimeSpan? RetryAfter) AttemptEmail(string email) =>
        Attempt(byEmail, email.Trim().ToLowerInvariant());

    public void Dispose()
    {
        byEmail.Dispose();
        byIp.Dispose();
    }

    private static PartitionedRateLimiter<string> CreateLimiter(int permitLimit) =>
        PartitionedRateLimiter.Create<string, string>(partitionKey =>
            RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = Window,
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true,
            }));

    private static (bool, TimeSpan?) Attempt(PartitionedRateLimiter<string> limiter, string partitionKey)
    {
        using var lease = limiter.AttemptAcquire(partitionKey);
        return lease.IsAcquired ? (true, null) : Rejected(lease);
    }

    private static (bool, TimeSpan?) Rejected(RateLimitLease lease) =>
        (false, lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter) ? retryAfter : Window);
}