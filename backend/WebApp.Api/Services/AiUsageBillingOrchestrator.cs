using Microsoft.EntityFrameworkCore;
using WebApp.Api.Data;
using WebApp.Api.Models;

namespace WebApp.Api.Services;

/// <summary>Resumable per-usage billing. Each debit service owns its transaction.</summary>
public sealed class AiUsageBillingOrchestrator(DbContextOptions<DiagLinkDbContext> options) : IAiUsageBillingOrchestrator
{
    public async Task<AiUsageBillingResult> ProcessAsync(Guid usageRecordId, CancellationToken ct = default)
    {
        await using var db = new DiagLinkDbContext(options);
        var result = new AiUsageBillingResult { UsageRecordId=usageRecordId };
        AiUsageBillingResult Fail(string status, string? reason=null) => result with
        { Status=status, FailureReason=reason ?? status, BillingPeriodRequired=status=="BillingPeriodNotFound",
            WalletCreditRequired=status is "AwaitingWalletCredit" or "WalletNotFound" };
        var usage = await db.AiUsageRecords.AsNoTracking().SingleOrDefaultAsync(u=>u.Id==usageRecordId,ct);
        if(usage == null) return Fail("UsageNotValuable", "UsageRecordNotFound");
        result = result with { CompanyId=usage.CompanyId, MachineId=usage.MachineId };
        var cost = await new AiCostCalculator(db).CalculateAsync(usage,ct);
        result = result with { ProviderCost=cost.RealAiCost, ProviderCurrency=cost.Currency };
        if(!cost.IsValuable || cost.RealAiCost == null) return Fail("UsageNotValuable",cost.FailureReason);
        var converted = await new AiCostCurrencyConverter(db).ConvertAsync(cost.RealAiCost.Value,cost.Currency,usage,ct);
        if(!converted.IsConvertible || converted.ConvertedAmount == null) return Fail("CurrencyConversionFailed",converted.FailureReason);
        var total = converted.ConvertedAmount.Value;
        result = result with { RealAiCostEur=total, RemainingRealAiCostEur=total };

        // One read retrieves both buckets. Do not infer settlement from a debit service status alone.
        async Task<bool> Refresh()
        {
            var entries=await db.CreditLedger.AsNoTracking().Where(e=>e.AiUsageRecordId==usageRecordId
                && e.EntryType=="AiUsage" && (e.BucketType=="MachineIncluded" || e.BucketType=="CompanyWallet")).ToListAsync(ct);
            if(entries.Any(e=>e.CompanyId!=usage.CompanyId || e.MachineId!=usage.MachineId || e.Currency!="EUR"
                || e.RealAiCost is not >= 0 || e.BalanceAfter is not >= 0)
                || entries.GroupBy(e=>e.BucketType).Any(g=>g.Count()>1)) return false;
            var machineEntry=entries.SingleOrDefault(e=>e.BucketType=="MachineIncluded");
            var walletEntry=entries.SingleOrDefault(e=>e.BucketType=="CompanyWallet");
            if(machineEntry!=null && (machineEntry.RealAiCost is not > 0 || machineEntry.CommercialCreditAmount!=null)) return false;
            if(walletEntry!=null && (walletEntry.RealAiCost is not > 0 || walletEntry.CommercialCreditAmount is not > 0)) return false;
            var machineCovered=machineEntry?.RealAiCost ?? 0m;
            var walletCovered=walletEntry?.RealAiCost ?? 0m;
            result = result with { MachineCoveredRealAiCostEur=machineCovered, WalletCoveredRealAiCostEur=walletCovered,
                RemainingRealAiCostEur=total-machineCovered-walletCovered,
                CommercialCreditDebitedEur=walletEntry?.CommercialCreditAmount ?? 0m,
                WalletBalanceAfterEur=walletEntry?.BalanceAfter,
                MachineLedgerEntryId=machineEntry?.Id, WalletLedgerEntryId=walletEntry?.Id };
            // A CompanyWallet entry must settle its full remainder under the all-or-nothing contract.
            return result.RemainingRealAiCostEur>=0 && (walletEntry==null || result.RemainingRealAiCostEur==0);
        }
        AiUsageBillingResult Done(string status) => result with { Success=true, Status=status, FailureReason=null,
            WalletCreditRequired=false, BillingPeriodRequired=false };
        if(!await Refresh()) return Fail("DataInconsistency");
        if(result.RemainingRealAiCostEur==0) return Done("AlreadyFullyProcessed");

        var machine=await new AiCreditConsumptionService(options).ConsumeAsync(usageRecordId,total,"EUR",ct);
        if(!await Refresh()) return Fail("DataInconsistency");
        if(result.RemainingRealAiCostEur==0)
            return Done(machine.Status=="AlreadyProcessed" ? "AlreadyFullyProcessed" :
                result.WalletCoveredRealAiCostEur>0 ? "ProcessedByMachineAndWallet" : "ProcessedByMachine");
        if(!machine.Success) return Fail(machine.Status,machine.FailureReason);
        if(machine.Status is not ("WalletDebitRequired" or "AlreadyProcessed")) return Fail("DataInconsistency");

        var wallet=await new CompanyWalletDebitService(options).DebitAsync(usageRecordId,result.RemainingRealAiCostEur,"EUR",ct);
        if(!await Refresh()) return Fail("DataInconsistency");
        if(result.RemainingRealAiCostEur==0)
            return Done(wallet.Status=="AlreadyProcessed" ? "AlreadyFullyProcessed" :
                result.MachineCoveredRealAiCostEur>0 ? "ProcessedByMachineAndWallet" : "ProcessedByWallet");
        result=result with { WalletBalanceAfterEur=wallet.Status=="WalletNotFound" ? null : wallet.WalletBalanceAfterEur };
        return wallet.Status=="InsufficientWalletBalance" ? Fail("AwaitingWalletCredit",wallet.Status)
            : Fail(wallet.Status,wallet.FailureReason);
    }
}
