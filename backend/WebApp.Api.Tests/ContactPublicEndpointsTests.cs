using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using WebApp.Api.Models;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

[TestClass]
public sealed class ContactPublicEndpointsTests
{
    [TestMethod]
    public async Task CreateAsync_ValidRequest_SendsConfirmedEmailWithReplyTo()
    {
        using var limiter = new ContactSubmissionRateLimiter();
        var sender = new Sender("acs-contact-42");

        var result = await SubmitAsync(ValidRequest() with
        {
            Name = "  Claire Martin  ",
            Email = "  claire@example.com  ",
        }, sender, limiter);

        Assert.AreEqual(StatusCodes.Status200OK, Status(result));
        Assert.AreEqual(1, sender.Calls);
        Assert.AreEqual("contact@example.test", sender.RecipientEmail);
        Assert.AreEqual("claire@example.com", sender.ReplyToEmail);
        Assert.AreEqual("Claire Martin", sender.ReplyToName);
        Assert.AreEqual("DiagLink — Nouveau message reçu via le formulaire de contact", sender.Subject);
        StringAssert.Contains(sender.TextBody!, "Entreprise : Ateliers Martin");
        StringAssert.Contains(sender.TextBody!, "Message :");
        StringAssert.Contains(sender.TextBody!, "Besoin d’une démonstration.");
    }

    [TestMethod]
    public async Task CreateAsync_OptionalFieldsMissing_StillSends()
    {
        using var limiter = new ContactSubmissionRateLimiter();
        var sender = new Sender("acs-contact-42");

        var result = await SubmitAsync(ValidRequest() with { Company = null, Phone = null }, sender, limiter);

        Assert.AreEqual(StatusCodes.Status200OK, Status(result));
        StringAssert.Contains(sender.TextBody!, "Entreprise : Non renseignée");
        StringAssert.Contains(sender.TextBody!, "Téléphone : Non renseigné");
    }

    [TestMethod]
    public async Task CreateAsync_InvalidFields_ReturnsValidationProblemWithoutSending()
    {
        using var limiter = new ContactSubmissionRateLimiter();
        var sender = new Sender("acs-contact-42");

        var invalidEmail = await SubmitAsync(ValidRequest() with { Email = "not-an-email" }, sender, limiter);
        var invalidPhone = await SubmitAsync(ValidRequest() with { Phone = "abc" }, sender, limiter);
        var oversizedMessage = await SubmitAsync(ValidRequest() with { Message = new string('x', 5001) }, sender, limiter);

        Assert.AreEqual(StatusCodes.Status400BadRequest, Status(invalidEmail));
        Assert.AreEqual(StatusCodes.Status400BadRequest, Status(invalidPhone));
        Assert.AreEqual(StatusCodes.Status400BadRequest, Status(oversizedMessage));
        Assert.AreEqual(0, sender.Calls);
    }

    [TestMethod]
    public async Task CreateAsync_NullRequest_ReturnsValidationProblemWithoutSending()
    {
        using var limiter = new ContactSubmissionRateLimiter();
        var sender = new Sender("acs-contact-42");
        var context = new DefaultHttpContext();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Contact:RecipientAddress"] = "contact@example.test" }).Build();

        var result = await ContactPublicEndpoints.CreateAsync(null, context, configuration, sender, limiter,
            NullLoggerFactory.Instance, default);

        Assert.AreEqual(StatusCodes.Status400BadRequest, Status(result));
        Assert.AreEqual(0, sender.Calls);
    }

    [TestMethod]
    public async Task CreateAsync_MissingRecipient_ReturnsServiceUnavailableWithoutSending()
    {
        using var limiter = new ContactSubmissionRateLimiter();
        var sender = new Sender("acs-contact-42");

        var result = await SubmitAsync(ValidRequest(), sender, limiter, recipient: null);

        Assert.AreEqual(StatusCodes.Status503ServiceUnavailable, Status(result));
        Assert.AreEqual(0, sender.Calls);
    }

    [TestMethod]
    public async Task CreateAsync_UnconfirmedOrFailedDelivery_ReturnsBadGateway()
    {
        using var unconfirmedLimiter = new ContactSubmissionRateLimiter();
        using var failedLimiter = new ContactSubmissionRateLimiter();
        var unconfirmed = await SubmitAsync(ValidRequest(), new Sender(null), unconfirmedLimiter);
        var failed = await SubmitAsync(ValidRequest(), new Sender(exception: new IOException("sensitive provider detail")), failedLimiter);

        Assert.AreEqual(StatusCodes.Status502BadGateway, Status(unconfirmed));
        Assert.AreEqual(StatusCodes.Status502BadGateway, Status(failed));
    }

    [TestMethod]
    public async Task CreateAsync_EmailLimitExceeded_ReturnsTooManyRequestsWithRetryAfter()
    {
        using var limiter = new ContactSubmissionRateLimiter();
        var sender = new Sender("acs-contact-42");
        Submission? rejected = null;

        for (var attempt = 0; attempt <= ContactSubmissionRateLimiter.EmailPermitLimit; attempt++)
            rejected = await SubmitDetailedAsync(ValidRequest(), sender, limiter);

        Assert.IsNotNull(rejected);
        Assert.AreEqual(StatusCodes.Status429TooManyRequests, Status(rejected.Result));
        Assert.IsTrue(rejected.Context.Response.Headers.ContainsKey("Retry-After"));
        Assert.AreEqual(ContactSubmissionRateLimiter.EmailPermitLimit, sender.Calls);
    }

    [TestMethod]
    public async Task CreateAsync_IpLimitExceededAcrossEmails_ReturnsTooManyRequests()
    {
        using var limiter = new ContactSubmissionRateLimiter();
        var sender = new Sender("acs-contact-42");
        Submission? rejected = null;

        for (var attempt = 0; attempt <= ContactSubmissionRateLimiter.IpPermitLimit; attempt++)
        {
            rejected = await SubmitDetailedAsync(
                ValidRequest() with { Email = $"client-{attempt}@example.test" }, sender, limiter,
                remoteIp: $"10.0.0.{attempt + 1}", forwardedFor: "198.51.100.7, 203.0.113.9");
        }

        Assert.IsNotNull(rejected);
        Assert.AreEqual(StatusCodes.Status429TooManyRequests, Status(rejected.Result));
        Assert.AreEqual(ContactSubmissionRateLimiter.IpPermitLimit, sender.Calls);
    }

    private static async Task<IResult> SubmitAsync(ContactRequest request, Sender sender,
        ContactSubmissionRateLimiter limiter, string? recipient = "contact@example.test") =>
        (await SubmitDetailedAsync(request, sender, limiter, recipient)).Result;

    private static async Task<Submission> SubmitDetailedAsync(ContactRequest request, Sender sender,
        ContactSubmissionRateLimiter limiter, string? recipient = "contact@example.test",
        string remoteIp = "192.0.2.10", string? forwardedFor = null)
    {
        var configurationValues = recipient is null
            ? new Dictionary<string, string?>()
            : new Dictionary<string, string?> { ["Contact:RecipientAddress"] = recipient };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(configurationValues).Build();
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
        if (forwardedFor is not null) context.Request.Headers["X-Forwarded-For"] = forwardedFor;
        var result = await ContactPublicEndpoints.CreateAsync(request, context, configuration, sender, limiter,
            NullLoggerFactory.Instance, default);
        return new(result, context);
    }

    private static int? Status(IResult result) => ((IStatusCodeHttpResult)result).StatusCode;

    private static ContactRequest ValidRequest() => new(
        "Claire Martin", "Ateliers Martin", "claire@example.com", "+33 6 12 34 56 78",
        "Besoin d’une démonstration.");

    private sealed record Submission(IResult Result, DefaultHttpContext Context);

    private sealed class Sender(string? operationId = null, Exception? exception = null)
        : ITransactionalEmailSender
    {
        public int Calls { get; private set; }
        public string? RecipientEmail { get; private set; }
        public string? Subject { get; private set; }
        public string? TextBody { get; private set; }
        public string? ReplyToEmail { get; private set; }
        public string? ReplyToName { get; private set; }

        public Task<string?> SendAsync(string recipientEmail, string subject, string textBody,
            string? htmlBody, CancellationToken cancellationToken,
            string? replyToEmail = null, string? replyToName = null)
        {
            Calls++;
            RecipientEmail = recipientEmail;
            Subject = subject;
            TextBody = textBody;
            ReplyToEmail = replyToEmail;
            ReplyToName = replyToName;
            return exception is null ? Task.FromResult(operationId) : Task.FromException<string?>(exception);
        }
    }
}