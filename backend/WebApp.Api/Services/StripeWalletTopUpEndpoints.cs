using Microsoft.EntityFrameworkCore;
using WebApp.Api.Data;
namespace WebApp.Api.Services;

public record StartWalletTopUpRequest(Guid RequestId, decimal Amount, string Currency = "EUR");
public record WalletTopUpOverview(decimal Balance, string Currency, bool WalletExists, bool Enabled, WalletTopUpResult[] Operations);
public static class StripeWalletTopUpEndpoints
{
    public static void MapStripeWalletTopUps(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/companies/{companyId:guid}/stripe/wallet-topups").RequireAuthorization("SuperAdminOnly");
        group.MapGet("", ReadAsync); group.MapPost("", StartAsync);
        app.MapPost("/api/stripe/webhooks/wallet-topups", StripeWalletTopUpWebhook.HandleHttpAsync).AllowAnonymous();
    }
    public static async Task<IResult> ReadAsync(Guid companyId, DiagLinkDbContext db, StripeBillingOptions settings, CancellationToken ct)
    {
        if (!await db.Companies.AnyAsync(c => c.Id == companyId, ct)) return Results.NotFound();
        var wallet = await db.CompanyWallets.AsNoTracking().SingleOrDefaultAsync(w => w.CompanyId == companyId, ct);
        var ops = await db.StripeWalletTopUps.AsNoTracking().Where(o => o.CompanyId == companyId).OrderByDescending(o => o.CreatedAtUtc).ToListAsync(ct);
        return Results.Ok(new WalletTopUpOverview(wallet?.Balance ?? 0m, wallet?.Currency ?? "EUR", wallet != null,
            CompanyWalletTopUpService.Configured(settings) && (wallet == null || wallet.Currency == "EUR"),
            ops.Select(o => CompanyWalletTopUpService.Result(o)).ToArray()));
    }
    public static async Task<IResult> StartAsync(Guid companyId, StartWalletTopUpRequest request, DiagLinkDbContext db,
        CompanyWalletTopUpService service, StripeBillingOptions settings, CancellationToken ct)
    {
        if (!StripeAdminEndpoints.TestActionsEnabled(settings)) return Results.Conflict(new { error = "Stripe test requis." });
        if (!await db.Companies.AnyAsync(c => c.Id == companyId, ct)) return Results.NotFound();
        try { return Results.Ok(await service.StartAsync(companyId, request.RequestId, request.Amount, request.Currency, ct)); }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException) { return Results.Conflict(new { error = "Recharge non confirmée : vérifiez la configuration et reprenez la même opération. Une réconciliation peut être nécessaire." }); }
        catch (Stripe.StripeException) { return Results.Json(new { error = "Stripe indisponible : reprenez la même recharge." }, statusCode: 502); }
    }
}
