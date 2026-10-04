using Microsoft.EntityFrameworkCore;
using WebApp.Api.Data;

namespace WebApp.Api.Services;

public record AdminMachineCredit(Guid Id,string Name,decimal? Used,decimal? Remaining,DateTime? ResetUtc,bool CreditBlocked,string Reason,bool Billable,bool HasCurrentPaidRights,DateTime? PaidRightsEndUtc);
public record AdminFinanceOverview(decimal WalletBalance,AdminMachineCredit[] Machines);
public record AdminFinanceTechnical(DateTime FromUtc,DateTime ToUtc,int UsageCount,int UnvaluedCount,int UnconvertedCount,
    decimal RealCostEur,decimal CommercialConsumedEur,decimal WalletCoveredRealCostEur,decimal EstimatedWalletMarginEur,
    long InputTokens,long OutputTokens,int UnknownTokens,object[] Breakdown,object[] Providers);

/// <summary>Read-only projections; all valuation uses the existing financial services.</summary>
public static class AdminFinanceReader
{
    public record PeriodFinance(decimal SubscriptionsPaidEur,decimal IncludedCreditGrantedEur,decimal TopUpsAddedEur,
        int Companies,int Machines,int Users);
    public static async Task<IResult> PeriodFinanceAsync(DateTimeOffset from,DateTimeOffset to,DiagLinkDbContext db,CancellationToken ct)
    {
        if(to<=from)return Results.BadRequest(new{message="Période invalide."});
        var start=from.UtcDateTime;var end=to.UtcDateTime;
        var usageInPeriod=db.AiUsageRecords.AsNoTracking().Where(u=>u.CreatedAtUtc>=start&&u.CreatedAtUtc<end);
        var machinePeriods=db.MachineBillingPeriods.AsNoTracking().Where(p=>p.PeriodStartUtc<end&&p.PeriodEndUtc>start
            &&(p.Status=="Active"||p.Status=="Closed"));
        var initialPaymentsInPeriod=db.MachineRequestPayments.AsNoTracking()
            .Where(p=>p.Status==WebApp.Api.Models.Entities.MachineRequestPaymentStatus.Captured
                &&p.CapturedAtUtc>=start&&p.CapturedAtUtc<end
                &&p.RequestKind!=WebApp.Api.Models.MachineRequestKind.AdditionalDocuments
                &&p.CompanyId!=null&&p.MachineId!=null&&p.ActivatedAtUtc!=null&&p.FirstPeriodEndUtc!=null
                &&p.ServiceAmountCents!=null
                &&p.ProvisioningStage>=WebApp.Api.Models.Entities.MachineRequestProvisioningStage.InitialPeriodCreated);
        var machines=await machinePeriods.Select(p=>p.MachineId).Distinct().CountAsync(ct);
        var users=await usageInPeriod.Where(u=>u.UserId!=null).Select(u=>u.UserId).Distinct().CountAsync(ct);
        var companyIds=await usageInPeriod.Where(u=>u.CompanyId!=null).Select(u=>u.CompanyId!.Value)
            .Union(db.Machines.Where(m=>machinePeriods.Any(p=>p.MachineId==m.Id)).Select(m=>m.CompanyId))
            .Union(db.StripeSubscriptionPayments.Where(p=>p.PaymentReference!=""&&p.AmountPaidCents>0
                &&((p.PeriodStartUtc<end&&p.PeriodEndUtc>start)||(p.PaymentConfirmedAtUtc>=start&&p.PaymentConfirmedAtUtc<end))).Select(p=>p.CompanyId))
            .Union(initialPaymentsInPeriod.Select(p=>p.CompanyId!.Value))
            .Union(db.StripeWalletTopUps.Where(o=>o.PaymentConfirmedAtUtc>=start&&o.PaymentConfirmedAtUtc<end
                &&(o.Stage==WebApp.Api.Models.Entities.StripeWalletTopUpStage.WalletCredited||o.Stage==WebApp.Api.Models.Entities.StripeWalletTopUpStage.Completed)
                &&db.CreditLedger.Any(e=>e.Id==o.LedgerEntryId&&e.CompanyId==o.CompanyId&&e.EntryType=="TopUp"&&e.BucketType=="CompanyWallet")).Select(o=>o.CompanyId))
            .CountAsync(ct);
        var payments=await db.StripeSubscriptionPayments.AsNoTracking()
            .Where(p=>p.PaymentConfirmedAtUtc>=start&&p.PaymentConfirmedAtUtc<end&&p.AmountPaidCents>0&&p.PaymentReference!="")
            .ToListAsync(ct);
        var initialPayments=await initialPaymentsInPeriod.ToListAsync(ct);
        var granted=new HashSet<Guid>();decimal included=0;
        foreach(var payment in payments)
        {
            // Stored machine snapshot plus the exact paid cycle identifies actual granted periods.
            var ids=System.Text.Json.JsonSerializer.Deserialize<Guid[]>(payment.MachineIdsJson)??[];
            var periods=await db.MachineBillingPeriods.AsNoTracking().Where(p=>ids.Contains(p.MachineId)
                &&p.PeriodStartUtc==payment.PeriodStartUtc&&p.PeriodEndUtc==payment.PeriodEndUtc
                &&db.Machines.Any(m=>m.Id==p.MachineId&&m.CompanyId==payment.CompanyId)).ToListAsync(ct);
            foreach(var period in periods)if(granted.Add(period.Id))included+=period.IncludedAiBudgetRealCost;
        }
        foreach(var payment in initialPayments)
        {
            var period=await db.MachineBillingPeriods.AsNoTracking().SingleOrDefaultAsync(p=>p.MachineId==payment.MachineId
                &&p.PeriodStartUtc==payment.ActivatedAtUtc&&p.PeriodEndUtc==payment.FirstPeriodEndUtc
                &&db.Machines.Any(m=>m.Id==p.MachineId&&m.CompanyId==payment.CompanyId),ct);
            if(period!=null&&granted.Add(period.Id))included+=period.IncludedAiBudgetRealCost;
        }
        var topups=await (from entry in db.CreditLedger.AsNoTracking()
            join op in db.StripeWalletTopUps.AsNoTracking() on entry.Id equals op.LedgerEntryId
            where entry.EntryType=="TopUp"&&entry.BucketType=="CompanyWallet"&&entry.Currency=="EUR"
                &&entry.CompanyId==op.CompanyId&&entry.CommercialCreditAmount>0
                &&op.PaymentConfirmedAtUtc>=start&&op.PaymentConfirmedAtUtc<end
                &&(op.Stage==WebApp.Api.Models.Entities.StripeWalletTopUpStage.WalletCredited||op.Stage==WebApp.Api.Models.Entities.StripeWalletTopUpStage.Completed)
            select entry.CommercialCreditAmount).SumAsync(ct)??0m;
        var subscriptionsPaid=(payments.Sum(p=>p.AmountPaidCents)+initialPayments.Sum(p=>(long)p.ServiceAmountCents!.Value))/100m;
        return Results.Ok(new PeriodFinance(subscriptionsPaid,included,topups,companyIds,machines,users));
    }
    public record GlobalTopUps(decimal TotalAddedEur);
    public static async Task<IResult> GlobalTopUpsAsync(DiagLinkDbContext db,CancellationToken ct)
    {
        // TopUp ledger entries are persisted atomically with the wallet credit, after payment confirmation.
        var total=await db.CreditLedger.AsNoTracking()
            .Where(e=>e.EntryType=="TopUp"&&e.BucketType=="CompanyWallet"&&e.Currency=="EUR"&&e.CommercialCreditAmount>0)
            .SumAsync(e=>e.CommercialCreditAmount,ct)??0m;
        return Results.Ok(new GlobalTopUps(total));
    }
    public static async Task<IResult> OverviewAsync(Guid companyId,DiagLinkDbContext db,AiCreditAccessService access,CancellationToken ct)
    {
        if(!await db.Companies.AnyAsync(c=>c.Id==companyId,ct))return Results.NotFound();
        var now=DateTime.UtcNow;
        var machines=await db.Machines.AsNoTracking().Where(m=>m.CompanyId==companyId).OrderBy(m=>m.Name).ToArrayAsync(ct);
        var periods=await db.MachineBillingPeriods.AsNoTracking().Where(p=>db.Machines.Any(m=>m.Id==p.MachineId&&m.CompanyId==companyId)&&(p.Status=="Active"||p.Status=="Closed")).ToArrayAsync(ct);
        var rows=new List<AdminMachineCredit>();
        foreach(var machine in machines)
        {
            var paid=periods.Where(p=>p.MachineId==machine.Id).ToArray();
            var matches=paid.Where(p=>p.PeriodStartUtc<=now&&now<p.PeriodEndUtc).ToArray();
            var p=matches.Length==1 && matches[0].Status == "Active"?matches[0]:null;
            var credit=await access.CheckAsync(machine.Id,ct);
            rows.Add(new(machine.Id,machine.Name,p?.IncludedAiUsedRealCost,p==null?0m:Math.Max(0,p.IncludedAiBudgetRealCost-p.IncludedAiUsedRealCost),
                p==null?null:DateTime.SpecifyKind(p.PeriodEndUtc,DateTimeKind.Utc),!credit.Allowed,credit.Message,
                machine.Status=="active",matches.Length>0,
                paid.Length==0?null:DateTime.SpecifyKind(paid.Max(p=>p.PeriodEndUtc),DateTimeKind.Utc)));
        }
        var wallet=await db.CompanyWallets.AsNoTracking().SingleOrDefaultAsync(w=>w.CompanyId==companyId,ct);
        return Results.Ok(new AdminFinanceOverview(wallet?.Balance??0,rows.ToArray()));
    }
    public static async Task<IResult> TechnicalAsync(Guid companyId,DiagLinkDbContext db,CancellationToken ct)
    {
        if(!await db.Companies.AnyAsync(c=>c.Id==companyId,ct))return Results.NotFound();
        // A bounded, explicit window keeps this diagnostic read usable without changing billing periods.
        var to=DateTime.UtcNow;var from=to.AddDays(-30);
        var usages=await db.AiUsageRecords.AsNoTracking().Where(u=>u.CompanyId==companyId&&u.CreatedAtUtc>=from&&u.CreatedAtUtc<to).ToArrayAsync(ct);
        decimal real=0;int unvalued=0,unconverted=0;
        var costs=new AiCostCalculator(db);var currency=new AiCostCurrencyConverter(db);
        foreach(var usage in usages)
        {
            var cost=await costs.CalculateAsync(usage,ct);
            if(!cost.IsValuable||cost.RealAiCost==null){unvalued++;continue;}
            var converted=await currency.ConvertAsync(cost.RealAiCost.Value,cost.Currency,usage,ct);
            if(!converted.IsConvertible||converted.ConvertedAmount==null){unconverted++;continue;}
            real+=converted.ConvertedAmount.Value;
        }
        var entries=await db.CreditLedger.AsNoTracking().Where(e=>e.CompanyId==companyId&&e.EntryType=="AiUsage"&&e.BucketType=="CompanyWallet"
            && db.AiUsageRecords.Any(u=>u.Id==e.AiUsageRecordId&&u.CompanyId==companyId&&u.CreatedAtUtc>=from&&u.CreatedAtUtc<to)).ToArrayAsync(ct);
        var commercial=entries.Sum(e=>e.CommercialCreditAmount??0);var covered=entries.Sum(e=>e.RealAiCost??0);
        return Results.Ok(new AdminFinanceTechnical(from,to,usages.Length,unvalued,unconverted,real,commercial,covered,commercial-covered,
            usages.Where(u=>u.Available).Sum(u=>(long?)u.InputTokens??0),usages.Where(u=>u.Available).Sum(u=>(long?)u.OutputTokens??0),
            usages.Count(u=>!u.Available||u.InputTokens==null||u.OutputTokens==null),
            usages.GroupBy(u=>u.UsageType).Select(g=>(object)new{Type=g.Key.ToString(),Count=g.Count()}).ToArray(),
            usages.GroupBy(u=>new{u.Provider,u.Model}).Select(g=>(object)new{g.Key.Provider,g.Key.Model,Count=g.Count()}).ToArray()));
    }
}
