namespace WebApp.Api.Services;

public sealed class StripeBillingOptions
{
    public bool Enabled { get; init; }
    public bool AllowLive { get; init; }
    public string SecretKey { get; init; } = "";
    public string PriceId { get; init; } = "";
    public string WebhookSecret { get; init; } = "";
    public string TopUpWebhookSecret { get; init; } = "";
    public string TopUpReturnUrl { get; init; } = "";
    public string MachineRequestWebhookSecret { get; init; } = "";
    public string MachineRequestReturnUrl { get; init; } = "";
    public string MachineRequestAppReturnUrl { get; init; } = "";

    public static StripeBillingOptions FromConfiguration(IConfiguration configuration) => new()
    {
        Enabled = string.Equals(configuration["STRIPE_ENABLED"], "true", StringComparison.OrdinalIgnoreCase),
        AllowLive = string.Equals(configuration["STRIPE_ALLOW_LIVE"], "true", StringComparison.OrdinalIgnoreCase),
        SecretKey = configuration["STRIPE_SECRET_KEY"] ?? "",
        PriceId = configuration["STRIPE_PRICE_ID"] ?? "",
        WebhookSecret = configuration["STRIPE_WEBHOOK_SECRET"] ?? "",
        TopUpWebhookSecret = configuration["STRIPE_TOPUP_WEBHOOK_SECRET"] ?? "",
        TopUpReturnUrl = configuration["STRIPE_TOPUP_RETURN_URL"] ?? "",
        MachineRequestWebhookSecret = configuration["STRIPE_MACHINE_REQUEST_WEBHOOK_SECRET"] ?? "",
        MachineRequestReturnUrl = configuration["STRIPE_MACHINE_REQUEST_RETURN_URL"] ?? "",
        MachineRequestAppReturnUrl = configuration["STRIPE_MACHINE_REQUEST_APP_RETURN_URL"] ?? ""
    };

    public void Validate()
    {
        if (!Enabled) throw new InvalidOperationException("Stripe billing is disabled.");
        var test = SecretKey.StartsWith("sk_test_", StringComparison.Ordinal) || SecretKey.StartsWith("rk_test_", StringComparison.Ordinal);
        var live = SecretKey.StartsWith("sk_live_", StringComparison.Ordinal) || SecretKey.StartsWith("rk_live_", StringComparison.Ordinal);
        if ((!test && !live) || (live && !AllowLive))
            throw new InvalidOperationException("Stripe secret key is missing, invalid, or live mode is not enabled.");
        if (!PriceId.StartsWith("price_", StringComparison.Ordinal))
            throw new InvalidOperationException("STRIPE_PRICE_ID is required.");
    }
}
