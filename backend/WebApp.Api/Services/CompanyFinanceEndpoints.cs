using Microsoft.EntityFrameworkCore;
using WebApp.Api.Data;
using WebApp.Api.Models;

namespace WebApp.Api.Services;

public record CompanyFinanceSummary(decimal? MachineCreditRemaining, decimal WalletBalance,
    string? SubscriptionStatus, DateTime? NextDueUtc, bool CancelAtPeriodEnd, decimal? UnpaidInvoiceAmount,
    bool RechargeEnabled, string? CreditStatus, string? CreditMessage)
{
    public DateTime? MachineCreditResetUtc { get; init; }
}
public record ClientTopUp(Guid Id, decimal Amount, string Status, string? PaymentUrl);

public static class CompanyFinanceEndpoints
{
    public static void MapCompanyFinance(this IEndpointRouteBuilder app)
    {
        var group=app.MapGroup("/api/company/finance").RequireAuthorization("CompanyAdminOnly");
        group.MapGet("", ReadAsync);
        group.MapPost("/invoice-payment", InvoicePaymentAsync);
        group.MapGet("/topups", ReadTopUpsAsync);
        group.MapPost("/topups", StartAsync);
    }
    // Claim enriched by the existing authentication middleware, never accepted from request JSON.
    private static Guid? Company(HttpContext context) => Guid.TryParse(context.User.FindFirst(DiagLinkClaimTypes.CompanyId)?.Value,out var id) ? id : null;
    public static async Task<IResult> InvoicePaymentAsync(HttpContext context, DiagLinkDbContext db,
        IStripeSubscriptionPaymentGateway gateway, CancellationToken ct)
    {
        var company=Company(context);if(company==null)return Results.Forbid();
        var account=await db.BillingAccounts.AsNoTracking().SingleOrDefaultAsync(a=>a.CompanyId==company,ct);
        if(account==null||string.IsNullOrEmpty(account.StripeCustomerId)||string.IsNullOrEmpty(account.StripeSubscriptionId)
            ||string.IsNullOrEmpty(account.LatestInvoiceId))return Results.Conflict(new{message="Aucune facture à régler."});
        try
        {
            // Existing gateway re-reads Stripe and validates customer, subscription and invoice ownership.
            var invoice=await gateway.ReadAsync(account,account.LatestInvoiceId,ct);
            if(invoice.Id!=account.LatestInvoiceId||invoice.SubscriptionId!=account.StripeSubscriptionId||invoice.Status!="open"
                ||!Uri.TryCreate(invoice.PaymentUrl,UriKind.Absolute,out var url)||url.Scheme!="https"
                ||url.Host!="invoice.stripe.com"||!string.IsNullOrEmpty(url.UserInfo))
                return Results.Conflict(new{message="Cette facture ne peut pas être réglée."});
            return Results.Ok(new {paymentUrl=url.AbsoluteUri});
        }
        catch(InvalidOperationException){return Results.Conflict(new{message="Facture indisponible ou non conforme. Actualisez le compte."});}
        catch(Stripe.StripeException){return Results.Json(new{message="Facture Stripe inaccessible. Réessayez plus tard."},statusCode:502);}
    }
    public static async Task<IResult> ReadAsync(Guid? machineId, HttpContext context, DiagLinkDbContext db,
        AiCreditAccessService credits, StripeBillingOptions settings, CancellationToken ct)
    {
        var company=Company(context); if(company==null)return Results.Forbid();
        if(machineId.HasValue && !await db.Machines.AnyAsync(m=>m.Id==machineId && m.CompanyId==company,ct))return Results.NotFound();
        var account=await db.BillingAccounts.AsNoTracking().SingleOrDefaultAsync(a=>a.CompanyId==company,ct);
        var wallet=await db.CompanyWallets.AsNoTracking().SingleOrDefaultAsync(w=>w.CompanyId==company,ct);
        var now=DateTime.UtcNow;
        var periods=await db.MachineBillingPeriods.AsNoTracking().Where(p=>p.MachineId==machineId && p.PeriodStartUtc<=now && now<p.PeriodEndUtc).ToListAsync(ct);
        decimal? remaining=machineId==null?null:0m;
        if(periods.Count==1 && periods[0].Status == "Active")remaining=Math.Max(0,periods[0].IncludedAiBudgetRealCost-periods[0].IncludedAiUsedRealCost);
        var credit=machineId.HasValue ? await credits.CheckAsync(machineId,ct) : null;
        return Results.Ok(new CompanyFinanceSummary(remaining,wallet?.Balance??0,account?.SubscriptionStatus,
            account?.CurrentPeriodEndUtc is DateTime end ? DateTime.SpecifyKind(end,DateTimeKind.Utc) : null,
            account?.CancelAtPeriodEnd??false,account?.LatestInvoiceStatus=="open" && account.AmountRemainingCents>0 ? account.AmountRemainingCents/100m : null,
            CompanyWalletTopUpService.Configured(settings) && (wallet==null || wallet.Currency=="EUR"),credit?.Status,credit?.Allowed==false?credit.Message:null)
        {
            MachineCreditResetUtc = periods.Count == 1 && periods[0].Status == "Active"
                ? DateTime.SpecifyKind(periods[0].PeriodEndUtc, DateTimeKind.Utc) : null
        });
    }
    public static async Task<IResult> ReadTopUpsAsync(HttpContext context,DiagLinkDbContext db,CancellationToken ct)
    {
        var company=Company(context);if(company==null)return Results.Forbid();
        var rows=await db.StripeWalletTopUps.AsNoTracking().Where(o=>o.CompanyId==company).OrderByDescending(o=>o.CreatedAtUtc).Take(10).ToListAsync(ct);
        return Results.Ok(rows.Select(o=>Project(CompanyWalletTopUpService.Result(o))).ToArray());
    }
    public static async Task<IResult> StartAsync(StartWalletTopUpRequest request,HttpContext context,
        CompanyWalletTopUpService service,CancellationToken ct)
    {
        var company=Company(context);if(company==null)return Results.Forbid();
        try{return Results.Ok(Project(await service.StartAsync(company.Value,request.RequestId,request.Amount,request.Currency,ct)));}
        catch(ArgumentException){return Results.BadRequest(new{message="Montant invalide : minimum 10 €, en EUR, avec deux décimales au maximum."});}
        catch(InvalidOperationException){return Results.Conflict(new{message="Recharge indisponible. Réessayez la même demande ou contactez votre administrateur."});}
        catch(Stripe.StripeException){return Results.Json(new{message="Paiement indisponible. Réessayez la même demande."},statusCode:502);}
    }
    public static ClientTopUp Project(WalletTopUpResult result)=>new(result.Id,result.AmountEur,
        result.Stage=="Completed"?"Completed":"Pending",result.Stage is "PaymentCreated" or "AwaitingPayment"?result.PaymentUrl:null);
}
