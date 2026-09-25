namespace WebApp.Api.Services;

public interface ITransactionalEmailSender
{
    Task<string?> SendAsync(string recipientEmail, string subject, string textBody,
        string? htmlBody, CancellationToken cancellationToken);
}
