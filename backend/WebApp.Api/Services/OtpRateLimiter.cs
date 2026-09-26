using System.Net;
using System.Threading.RateLimiting;

namespace WebApp.Api.Services;

public sealed record OtpRateLimitDecision(bool IsAllowed, TimeSpan? RetryAfter);

public sealed class OtpRateLimiter : IDisposable
{
    public const int RequestCodeEmailPermitLimit = 5;
    public const int RequestCodeIpPermitLimit = 30;
    public const int VerifyCodeEmailPermitLimit = 10;
    public const int VerifyCodeIpPermitLimit = 100;
    public static readonly TimeSpan RequestCodeWindow = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan VerifyCodeWindow = TimeSpan.FromMinutes(10);

    private readonly PartitionedRateLimiter<string> requestCodeByEmail =
        CreateLimiter(RequestCodeEmailPermitLimit, RequestCodeWindow);
    private readonly PartitionedRateLimiter<string> requestCodeByIp =
        CreateLimiter(RequestCodeIpPermitLimit, RequestCodeWindow);
    private readonly PartitionedRateLimiter<string> verifyCodeByEmail =
        CreateLimiter(VerifyCodeEmailPermitLimit, VerifyCodeWindow);
    private readonly PartitionedRateLimiter<string> verifyCodeByIp =
        CreateLimiter(VerifyCodeIpPermitLimit, VerifyCodeWindow);

    public OtpRateLimitDecision AttemptRequestCode(IPAddress? remoteIpAddress, string? email) =>
        Attempt(requestCodeByIp, IpKey(remoteIpAddress), requestCodeByEmail, EmailKey(email), RequestCodeWindow);

    public OtpRateLimitDecision AttemptVerifyCode(IPAddress? remoteIpAddress, string? email) =>
        Attempt(verifyCodeByIp, IpKey(remoteIpAddress), verifyCodeByEmail, EmailKey(email), VerifyCodeWindow);

    public void Dispose()
    {
        requestCodeByEmail.Dispose();
        requestCodeByIp.Dispose();
        verifyCodeByEmail.Dispose();
        verifyCodeByIp.Dispose();
    }

    private static PartitionedRateLimiter<string> CreateLimiter(int permitLimit, TimeSpan window) =>
        PartitionedRateLimiter.Create<string, string>(partitionKey =>
            RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = window,
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true,
            }));

    private static OtpRateLimitDecision Attempt(
        PartitionedRateLimiter<string> ipLimiter,
        string ipKey,
        PartitionedRateLimiter<string> emailLimiter,
        string emailKey,
        TimeSpan fallbackRetryAfter)
    {
        using var ipLease = ipLimiter.AttemptAcquire(ipKey);
        if (!ipLease.IsAcquired) return Rejected(ipLease, fallbackRetryAfter);

        using var emailLease = emailLimiter.AttemptAcquire(emailKey);
        return emailLease.IsAcquired
            ? new OtpRateLimitDecision(true, null)
            : Rejected(emailLease, fallbackRetryAfter);
    }

    private static OtpRateLimitDecision Rejected(RateLimitLease lease, TimeSpan fallbackRetryAfter) =>
        new(false, lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
            ? retryAfter
            : fallbackRetryAfter);

    private static string IpKey(IPAddress? remoteIpAddress) => remoteIpAddress?.ToString() ?? "unknown";

    private static string EmailKey(string? email) => email?.Trim().ToLowerInvariant() ?? string.Empty;
}