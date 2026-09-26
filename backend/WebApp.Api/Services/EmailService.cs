using Azure;
using Azure.Communication.Email;
using Azure.Core;
using Azure.Identity;

namespace WebApp.Api.Services;

/// <summary>
/// Sends transactional email via Azure Communication Services Email, authenticated with
/// Entra ID (Managed Identity / Azure CLI) — no connection string or access key is ever used.
/// </summary>
public class EmailService : ITransactionalEmailSender
{
    private readonly EmailClient? _emailClient;
    private readonly string _senderAddress;
    private readonly string? _acsEndpoint;
    private readonly string _credentialKind;
    private readonly string? _managedIdentityClientId;
    private readonly bool _devNoAcs;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration configuration, ILogger<EmailService> logger, IHostEnvironment environment)
    {
        _logger = logger;

        // Read ACS endpoint and sender address. In Development, allow missing ACS and fall back to logging.
        var acsEndpointValue = configuration["Email:AcsEndpoint"];
        // Treat missing or placeholder values as not configured in Development.
        var isPlaceholder = !string.IsNullOrEmpty(acsEndpointValue) && acsEndpointValue.StartsWith("PLACEHOLDER", StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(acsEndpointValue) || isPlaceholder)
        {
            if (environment.IsDevelopment())
            {
                _devNoAcs = true;
                _acsEndpoint = null;
            }
            else
            {
                throw new InvalidOperationException("Email:AcsEndpoint is not configured");
            }
        }
        else
        {
            _acsEndpoint = acsEndpointValue;
        }

        _senderAddress = configuration["Email:SenderAddress"]
            ?? (environment.IsDevelopment() ? "no-reply@local.test" : throw new InvalidOperationException("Email:SenderAddress is not configured"));

        _managedIdentityClientId = configuration["MANAGED_IDENTITY_CLIENT_ID"]
            ?? configuration["OBO_MANAGED_IDENTITY_CLIENT_ID"]; // backward compat

        // Same environment-aware credential strategy as AgentFrameworkService.
        TokenCredential credential;
        if (environment.IsDevelopment())
        {
            credential = new ChainedTokenCredential(
                new AzureCliCredential(),
                new AzureDeveloperCliCredential());
            _credentialKind = "DevCredential(AzureCli/AzureDeveloperCli)";
        }
        else if (!string.IsNullOrEmpty(_managedIdentityClientId))
        {
            credential = new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(_managedIdentityClientId));
            _credentialKind = "ManagedIdentityCredential(UserAssigned)";
        }
        else
        {
            credential = new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned);
            _credentialKind = "ManagedIdentityCredential(SystemAssigned)";
        }

        if (!_devNoAcs && _acsEndpoint is not null)
        {
            _emailClient = new EmailClient(new Uri(_acsEndpoint), credential);
        }
        else
        {
            _emailClient = null;
        }
    }

    /// <summary>
    /// Sends the plaintext OTP login code to the user. Never call with an already-hashed code.
    /// </summary>
    public async Task SendLoginCodeAsync(string recipientEmail, string code, CancellationToken cancellationToken)
    {
        var subject = "Votre code de connexion DiagLink";
        var body =
            $"Votre code de connexion DiagLink est : {code}\n\n" +
            "Ce code est valable pendant 10 minutes.\n\n" +
            "Si vous n'êtes pas à l'origine de cette demande, vous pouvez ignorer ce message.";

        var emailMessage = new EmailMessage(
            senderAddress: _senderAddress,
            recipientAddress: recipientEmail,
            content: new EmailContent(subject) { PlainText = body });

        _logger.LogInformation(
            "OTP email send starting. Recipient={MaskedRecipient} AcsEndpoint={AcsEndpoint} Sender={Sender} Credential={CredentialKind} ManagedIdentityClientId={ManagedIdentityClientId} DevNoAcs={DevNoAcs}",
            MaskEmail(recipientEmail), _acsEndpoint ?? "(none)", _senderAddress, _credentialKind, _managedIdentityClientId ?? "(none)", _devNoAcs);

        if (_devNoAcs)
        {
            // Development fallback: log the code instead of calling ACS so local dev works without ACS provisioned.
            _logger.LogInformation("[DEV] OTP code for {Recipient} = {Code}", MaskEmail(recipientEmail), code);
            return;
        }

        try
        {
            // WaitUntil.Completed surfaces send failures immediately instead of fire-and-forget.
            await _emailClient!.SendAsync(Azure.WaitUntil.Completed, emailMessage, cancellationToken);
        }
        catch (RequestFailedException rfe)
        {
            // Never log the OTP code itself; only ACS-provided diagnostic fields.
            _logger.LogError(
                "OTP email send failed via ACS. Status={Status} ErrorCode={ErrorCode} ExceptionType={ExceptionType} Message={Message}",
                rfe.Status, rfe.ErrorCode, rfe.GetType().Name, rfe.Message);
            throw;
        }
        catch (Exception ex)
        {
            // Never log the OTP code itself.
            _logger.LogError(ex, "Failed to send login code email");
            throw;
        }
    }

    public async Task<string?> SendAsync(string recipientEmail, string subject, string textBody,
        string? htmlBody, CancellationToken cancellationToken,
        string? replyToEmail = null, string? replyToName = null)
    {
        var content = new EmailContent(subject) { PlainText = textBody };
        if (!string.IsNullOrWhiteSpace(htmlBody)) content.Html = htmlBody;
        var message = new EmailMessage(_senderAddress, recipientEmail, content);
        if (!string.IsNullOrWhiteSpace(replyToEmail))
            message.ReplyTo.Add(new EmailAddress(replyToEmail, replyToName));

        if (_devNoAcs)
        {
            _logger.LogInformation("[DEV] Transactional email suppressed. Recipient={MaskedRecipient} Subject={Subject}",
                MaskEmail(recipientEmail), subject);
            return null;
        }

        var operation = await _emailClient!.SendAsync(WaitUntil.Completed, message, cancellationToken);
        return operation.Id;
    }

    private static string MaskEmail(string email)
    {
        var atIndex = email.IndexOf('@');
        if (atIndex <= 1) return "***";
        return $"{email[0]}***{email[(atIndex - 1)..]}";
    }
}
