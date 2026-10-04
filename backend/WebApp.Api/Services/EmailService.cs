using Azure;
using Azure.Communication.Email;
using Azure.Core;
using Azure.Identity;
using System.Net;

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
        var emailMessage = new EmailMessage(
            senderAddress: _senderAddress,
            recipientAddress: recipientEmail,
            content: BuildLoginCodeContent(code));

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

    internal static EmailContent BuildLoginCodeContent(string code)
    {
        const string subject = "Votre code de connexion DiagLink";
        const string logoUrl = "https://diaglink.com/assets/Logo%20DiagLink.png";
        const string loginUrl = "https://app.diaglink.com/login";
        var plainText =
            $"Votre code de connexion DiagLink est : {code}\n\n" +
            "Ce code est valable pendant 10 minutes.\n\n" +
            $"Connexion : {loginUrl}\n\n" +
            "Si vous n'êtes pas à l'origine de cette demande, vous pouvez ignorer ce message.";
        var encodedCode = WebUtility.HtmlEncode(code);
        var html = $"""
            <!doctype html>
            <html lang="fr">
              <body style="margin:0; padding:0; background-color:#f3f4f6; color:#1f2937; font-family:Arial,Helvetica,sans-serif;">
                <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="width:100%; background-color:#f3f4f6;">
                  <tr>
                    <td align="center" style="padding:24px 16px;">
                      <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="width:100%; max-width:560px; background-color:#ffffff; border:1px solid #e5e7eb; border-radius:12px;">
                        <tr>
                          <td align="center" style="padding:32px 24px 12px;">
                            <img src="{logoUrl}" alt="DiagLink" width="190" style="display:block; width:100%; max-width:190px; height:auto; border:0;" />
                          </td>
                        </tr>
                        <tr>
                          <td align="center" style="padding:12px 24px 32px;">
                            <h1 style="margin:0 0 16px; color:#111827; font-size:24px; line-height:32px; font-weight:700;">Votre code de connexion</h1>
                            <p style="margin:0 0 24px; color:#4b5563; font-size:16px; line-height:24px;">Utilisez le code ci-dessous pour vous connecter à DiagLink.</p>
                            <div style="margin:0 auto 20px; padding:16px 12px; background-color:#f3f4f6; border:1px solid #d1d5db; border-radius:8px; color:#111827; font-size:34px; line-height:42px; font-weight:700; letter-spacing:6px; text-align:center; white-space:nowrap;">{encodedCode}</div>
                            <p style="margin:0; color:#374151; font-size:15px; line-height:22px;">Ce code est valable pendant 10 minutes.</p>
                            <div style="height:1px; margin:28px 0 20px; background-color:#e5e7eb; line-height:1px;">&nbsp;</div>
                            <p style="margin:0; color:#6b7280; font-size:13px; line-height:20px;">Si vous n'êtes pas à l'origine de cette demande, vous pouvez ignorer cet e-mail.</p>
                            <a href="{loginUrl}" style="display:inline-block; margin-top:24px; padding:10px 18px; background-color:#1f4e79; border-radius:6px; color:#ffffff; font-size:14px; line-height:20px; font-weight:700; text-decoration:none;">Se connecter à DiagLink</a>
                          </td>
                        </tr>
                        <tr>
                          <td align="center" style="padding:18px 24px; border-top:1px solid #e5e7eb; color:#6b7280; font-size:12px; line-height:18px;">DiagLink · Assistant Technique</td>
                        </tr>
                      </table>
                    </td>
                  </tr>
                </table>
              </body>
            </html>
            """;

        return new EmailContent(subject)
        {
            PlainText = plainText,
            Html = html
        };
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
