using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using WebApp.Api.Data;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;

namespace WebApp.Api.Services;

/// <summary>Read-only usage valuation; never persists or replays billing.</summary>
public static class AdminConsumptionReader
{
    private static readonly JsonSerializerOptions CallBreakdownJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public record Metrics(int Responses, int Vision, int Summaries, long Input, long Output, long Tokens,
        long CacheReadInput, long CacheCreationInput, long CacheCreation5mInput, long CacheCreation1hInput,
        int Unknown, int Unvalued, decimal RealCost, decimal CommercialCredit, decimal WalletRealCost,
        object[] Providers);
    public record UserMetrics(int Responses, int Vision, int Summaries, long Input, long Output, long Tokens,
        long CacheReadInput, long CacheCreationInput, long CacheCreation5mInput, long CacheCreation1hInput,
        int Unknown, int Unvalued, decimal RealCost, decimal CommercialCredit, decimal WalletRealCost,
        object[] Providers, decimal IncludedQuotaConsumed);
    public record UserRow(Guid? Id, string Name, UserMetrics Metrics);
    public record MachineRow(Guid? Id, string Name, bool Billable, decimal Budget, decimal Used,
        decimal Remaining, DateTime? ResetUtc, DateTime? RightsEndUtc, bool HasPaidRights,
        Metrics Metrics, UserRow[] Users);
    public record Report(string CompanyName, decimal WalletBalance, Metrics Metrics, MachineRow[] Machines);

    public record MachineTokenHistoryItem(
        DateTime CreatedAtUtc,
        int InputTokens,
        int OutputTokens,
        int TotalTokens,
        int CacheReadInputTokens,
        int CacheCreationInputTokens,
        int CacheCreation5mInputTokens,
        int CacheCreation1hInputTokens,
        string? Model,
        string? Provider,
        MachineTokenHistoryCall[]? Calls);

    public record MachineTokenHistoryCall(
        int CallNumber,
        long InputTokens,
        long OutputTokens,
        long TotalTokens,
        long CacheReadInputTokens,
        long CacheCreationInputTokens,
        long CacheCreation5mInputTokens,
        long CacheCreation1hInputTokens,
        int WebSearchRequests,
        string Model,
        string StopReason,
        string[] Tools,
        string[]? LoadedSkills,
        string[]? SkillContinuationFor)
    {
        public decimal? EstimatedCostEur { get; init; }
    }

    public record MachineTokenHistoryResponse(
        MachineTokenHistoryItem[] Items,
        bool HasMore);
    public static async Task<IResult> ReadAsync(Guid companyId, string? from, string? to, string? usageType,
        DiagLinkDbContext db, CancellationToken ct)
    {
        if (!AiUsageFilter.TryParse(from, to, usageType, out var filter)) return Results.BadRequest();
        var report = await ReadReportAsync(companyId, filter, db, ct);
        return report == null ? Results.NotFound() : Results.Ok(report);
    }

    public static async Task<IResult> ReadMachineTokenHistoryAsync(
        Guid companyId,
        Guid machineId,
        int? skip,
        int? take,
        DiagLinkDbContext db,
        CancellationToken ct)
    {
        var offset = skip ?? 0;
        var pageSize = take ?? 20;

        if (offset < 0 || pageSize < 1 || pageSize > 100)
            return Results.BadRequest(new
        {
            error = "Pagination invalide."
        });

     var machineExists = await db.Machines
         .AsNoTracking()
         .AnyAsync(
             machine => machine.Id == machineId
                 && machine.CompanyId == companyId,
             ct);

     if (!machineExists)
         return Results.NotFound();

     var rows = await db.AiUsageRecords
         .AsNoTracking()
         .Where(usage =>
             usage.CompanyId == companyId
             && usage.MachineId == machineId
             && usage.UsageType == AiUsageType.ChatResponse)
         .OrderByDescending(usage => usage.CreatedAtUtc)
         .Skip(offset)
         .Take(pageSize + 1)
         .Select(usage => new
         {
             usage.CreatedAtUtc,
             InputTokens = usage.InputTokens ?? 0,
             OutputTokens = usage.OutputTokens ?? 0,
             TotalTokens = usage.TotalTokens ?? 0,
             CacheReadInputTokens = usage.CacheReadInputTokens ?? 0,
             CacheCreationInputTokens = usage.CacheCreationInputTokens ?? 0,
             CacheCreation5mInputTokens = usage.CacheCreation5mInputTokens ?? 0,
             CacheCreation1hInputTokens = usage.CacheCreation1hInputTokens ?? 0,
             usage.Model,
             usage.Provider,
             usage.CallBreakdownJson
         })
         .ToArrayAsync(ct);

     var hasMore = rows.Length > pageSize;
     var visibleRows = rows.Take(pageSize).ToArray();
     var providers = visibleRows.Select(row => row.Provider).Where(value => value != null).Distinct().ToArray();
     var models = visibleRows.Select(row => row.Model).Where(value => value != null).Distinct().ToArray();
     var pricing = providers.Length == 0 || models.Length == 0
         ? []
         : await db.AiPricing.AsNoTracking()
             .Where(price => providers.Contains(price.Provider) && models.Contains(price.Model))
             .ToArrayAsync(ct);
     var exchangeRates = await db.ExchangeRates.AsNoTracking().ToArrayAsync(ct);

     return Results.Ok(new MachineTokenHistoryResponse(
          visibleRows.Select(row => new MachineTokenHistoryItem(
              row.CreatedAtUtc,
              row.InputTokens,
              row.OutputTokens,
              row.TotalTokens,
              row.CacheReadInputTokens,
              row.CacheCreationInputTokens,
              row.CacheCreation5mInputTokens,
              row.CacheCreation1hInputTokens,
              row.Model,
              row.Provider,
              ValueCalls(ReadCallBreakdown(row.CallBreakdownJson), row.Provider, row.CreatedAtUtc, pricing, exchangeRates))).ToArray(),
         hasMore));
    }

    private static MachineTokenHistoryCall[]? ValueCalls(
        MachineTokenHistoryCall[]? calls,
        string? provider,
        DateTime createdAtUtc,
        IReadOnlyCollection<AiPricing> pricing,
        IReadOnlyCollection<ExchangeRate> exchangeRates)
    {
        if (calls is null || string.IsNullOrWhiteSpace(provider)) return calls;
        return calls.Select(call =>
        {
            if (call.InputTokens > int.MaxValue || call.OutputTokens > int.MaxValue || call.TotalTokens > int.MaxValue ||
                call.CacheReadInputTokens > int.MaxValue || call.CacheCreationInputTokens > int.MaxValue ||
                call.CacheCreation5mInputTokens > int.MaxValue || call.CacheCreation1hInputTokens > int.MaxValue)
                return call;
            var usage = new AiUsageRecord
            {
                Id = Guid.Empty,
                UsageType = AiUsageType.ChatResponse,
                Available = true,
                Completed = true,
                Provider = provider,
                Model = call.Model,
                InputTokens = (int)call.InputTokens,
                OutputTokens = (int)call.OutputTokens,
                TotalTokens = checked((int)call.TotalTokens),
                CacheReadInputTokens = (int)call.CacheReadInputTokens,
                CacheCreationInputTokens = (int)call.CacheCreationInputTokens,
                CacheCreation5mInputTokens = (int)call.CacheCreation5mInputTokens,
                CacheCreation1hInputTokens = (int)call.CacheCreation1hInputTokens,
                WebSearchRequests = call.WebSearchRequests,
                CreatedAtUtc = createdAtUtc
            };
            var cost = AiCostCalculator.Calculate(usage, pricing);
            if (!cost.IsValuable || cost.RealAiCost is null) return call;
            var converted = AiCostCurrencyConverter.Convert(cost.RealAiCost.Value, cost.Currency, createdAtUtc, exchangeRates);
            return converted.IsConvertible ? call with { EstimatedCostEur = converted.ConvertedAmount } : call;
        }).ToArray();
    }

    private static MachineTokenHistoryCall[]? ReadCallBreakdown(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            return JsonSerializer.Deserialize<MachineTokenHistoryCall[]>(value, CallBreakdownJsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static async Task<Report?> ReadReportAsync(Guid companyId, AiUsageFilter filter,
        DiagLinkDbContext db, CancellationToken ct)
    {
        var company = await db.Companies.AsNoTracking().SingleOrDefaultAsync(c => c.Id == companyId, ct);
        if (company == null) return null;
        var query = db.AiUsageRecords.AsNoTracking().Where(u => u.CompanyId == companyId && u.CreatedAtUtc < filter.To.UtcDateTime);
        if (filter.From != null) query = query.Where(u => u.CreatedAtUtc >= filter.From.Value.UtcDateTime);
        if (filter.UsageType != null) query = query.Where(u => u.UsageType == filter.UsageType);
        var usages = await query.ToArrayAsync(ct);
        var ledger = await db.CreditLedger.AsNoTracking().Where(e => e.CompanyId == companyId
            && e.EntryType == "AiUsage" && (e.BucketType == "MachineIncluded" || e.BucketType == "CompanyWallet") && e.Currency == "EUR"
            && query.Any(u => u.Id == e.AiUsageRecordId && u.MachineId == e.MachineId)).ToArrayAsync(ct);
        var walletDebits = ledger.Where(e => e.BucketType == "CompanyWallet").ToLookup(e => e.AiUsageRecordId);
        var includedDebits = ledger.Where(e => e.BucketType == "MachineIncluded").ToLookup(e => e.AiUsageRecordId);

        var priceableUsages = usages.Where(usage =>
            AiCostCalculator.Calculate(usage, []).FailureReason == "PricingNotFound").ToArray();
        IReadOnlyCollection<AiPricing> pricing = [];
        if (priceableUsages.Length > 0)
        {
            var providers = priceableUsages.Select(usage => usage.Provider!).Distinct(StringComparer.Ordinal).ToArray();
            var models = priceableUsages.Select(usage => usage.Model!).Distinct(StringComparer.Ordinal).ToArray();
            var usageTypes = priceableUsages.Select(usage => usage.UsageType.ToString()).Distinct(StringComparer.Ordinal).ToArray();
            var earliestUsageUtc = priceableUsages.Min(usage => usage.CreatedAtUtc);
            var latestUsageUtc = priceableUsages.Max(usage => usage.CreatedAtUtc);
            pricing = await db.AiPricing.AsNoTracking().Where(price =>
                providers.Contains(price.Provider)
                && models.Contains(price.Model)
                && (price.UsageType == null || usageTypes.Contains(price.UsageType))
                && price.EffectiveFromUtc <= latestUsageUtc
                && (price.EffectiveToUtc == null || earliestUsageUtc < price.EffectiveToUtc)).ToArrayAsync(ct);
        }

        var calculated = usages.ToDictionary(usage => usage.Id, usage => AiCostCalculator.Calculate(usage, pricing));
        var ratesNeeded = usages.Where(usage =>
        {
            var cost = calculated[usage.Id];
            return cost.IsValuable && cost.RealAiCost != null
                && AiCostCurrencyConverter.Convert(cost.RealAiCost.Value, cost.Currency, usage.CreatedAtUtc, [])
                    .FailureReason == "ExchangeRateNotFound";
        }).ToArray();
        IReadOnlyCollection<ExchangeRate> exchangeRates = [];
        if (ratesNeeded.Length > 0)
        {
            var currencies = ratesNeeded.Select(usage => calculated[usage.Id].Currency!).Distinct(StringComparer.Ordinal).ToArray();
            var earliestUsageUtc = ratesNeeded.Min(usage => usage.CreatedAtUtc);
            var latestUsageUtc = ratesNeeded.Max(usage => usage.CreatedAtUtc);
            exchangeRates = await db.ExchangeRates.AsNoTracking().Where(rate =>
                currencies.Contains(rate.BaseCurrency)
                && rate.QuoteCurrency == "EUR"
                && rate.EffectiveFromUtc <= latestUsageUtc
                && (rate.EffectiveToUtc == null || earliestUsageUtc < rate.EffectiveToUtc)).ToArrayAsync(ct);
        }

        var valued = new Dictionary<Guid, decimal?>();
        foreach (var usage in usages)
        {
            ct.ThrowIfCancellationRequested();
            var cost = calculated[usage.Id];
            decimal? eur = null;
            if (cost.IsValuable && cost.RealAiCost != null)
            {
                var converted = AiCostCurrencyConverter.Convert(
                    cost.RealAiCost.Value, cost.Currency, usage.CreatedAtUtc, exchangeRates);
                if (converted.IsConvertible) eur = converted.ConvertedAmount;
            }
            valued[usage.Id] = eur;
        }
        Metrics Aggregate(AiUsageRecord[] rows) => new(
            rows.Count(u => u.UsageType == AiUsageType.ChatResponse), rows.Count(u => u.UsageType == AiUsageType.VisionTool),
            rows.Count(u => u.UsageType == AiUsageType.ConversationSummary),
            rows.Where(u => u.Available).Sum(u => (long?)u.InputTokens ?? 0),
            rows.Where(u => u.Available).Sum(u => (long?)u.OutputTokens ?? 0),
            rows.Where(u => u.Available).Sum(u => (long?)u.TotalTokens ?? 0),
            rows.Where(u => u.Available).Sum(u => (long?)u.CacheReadInputTokens ?? 0),
            rows.Where(u => u.Available).Sum(u => (long?)u.CacheCreationInputTokens ?? 0),
            rows.Where(u => u.Available).Sum(u => (long?)u.CacheCreation5mInputTokens ?? 0),
            rows.Where(u => u.Available).Sum(u => (long?)u.CacheCreation1hInputTokens ?? 0),
            rows.Count(u => !u.Available || u.InputTokens == null || u.OutputTokens == null),
            rows.Count(u => valued[u.Id] == null), rows.Sum(u => valued[u.Id] ?? 0),
            rows.Sum(u => walletDebits[u.Id].Sum(e => e.CommercialCreditAmount ?? 0)),
            rows.Sum(u => walletDebits[u.Id].Sum(e => e.RealAiCost ?? 0)),
            rows.GroupBy(u => new { u.Provider, u.Model }).Select(g => (object)new { g.Key.Provider, g.Key.Model, Count = g.Count() }).ToArray());
        UserMetrics AggregateUser(AiUsageRecord[] rows)
        {
            var metrics = Aggregate(rows);
            return new(metrics.Responses, metrics.Vision, metrics.Summaries, metrics.Input, metrics.Output, metrics.Tokens,
                metrics.CacheReadInput, metrics.CacheCreationInput, metrics.CacheCreation5mInput, metrics.CacheCreation1hInput,
                metrics.Unknown, metrics.Unvalued, metrics.RealCost, metrics.CommercialCredit, metrics.WalletRealCost,
                metrics.Providers, rows.Sum(u => includedDebits[u.Id].Sum(e => e.RealAiCost ?? 0)));
        }
        var machines = await db.Machines.AsNoTracking().Where(m => m.CompanyId == companyId).OrderBy(m => m.Name).ToArrayAsync(ct);
        var periods = await db.MachineBillingPeriods.AsNoTracking().Where(p => db.Machines.Any(m => m.CompanyId == companyId && m.Id == p.MachineId)
            && (p.Status == "Active" || p.Status == "Closed")).ToArrayAsync(ct);
        var users = await db.Users.AsNoTracking().Where(u => u.CompanyId == companyId).ToDictionaryAsync(u => u.Id, ct);
        var machineIds = machines.Select(m => m.Id).ToArray();
        var assignedUsers = await (from access in db.UserMachineAccess.AsNoTracking()
            join user in db.Users.AsNoTracking() on access.UserId equals user.Id
            where machineIds.Contains(access.MachineId) && user.CompanyId == companyId && user.Status == "active"
            select new { access.MachineId, User = user }).ToArrayAsync(ct);
        var activeCompanyAdminIds = users.Values.Where(user => user.Status == "active" && user.Role == DiagLinkRoles.CompanyAdmin)
            .Select(user => (Guid?)user.Id).ToArray();
        var now = DateTime.UtcNow;
        static DateTime? Utc(DateTime? date) => date == null ? null : DateTime.SpecifyKind(date.Value, DateTimeKind.Utc);
        var ids = machines.Select(m => (Guid?)m.Id).Concat(usages.Select(u => u.MachineId)).Distinct();
        var result = ids.Select(id =>
        {
            var machine = machines.SingleOrDefault(m => m.Id == id);
            var history = periods.Where(p => p.MachineId == id).ToArray();
            var current = history.Where(p => p.PeriodStartUtc <= now && now < p.PeriodEndUtc).ToArray();
            var period = current.Length == 1 && current[0].Status == "Active" ? current[0] : null;
            var rows = usages.Where(u => u.MachineId == id).ToArray();
            var usageByUser = rows.GroupBy(u => u.UserId).ToArray();
            var userIds = assignedUsers.Where(assignment => assignment.MachineId == id)
                .Select(assignment => (Guid?)assignment.User.Id).Concat(activeCompanyAdminIds)
                .Concat(usageByUser.Select(group => group.Key)).Distinct();
            return new MachineRow(id, machine?.Name ?? "Machine non attribuée / supprimée", machine?.Status == "active",
                period?.IncludedAiBudgetRealCost ?? 0, period?.IncludedAiUsedRealCost ?? 0,
                period == null ? 0 : Math.Max(0, period.IncludedAiBudgetRealCost - period.IncludedAiUsedRealCost),
                Utc(period?.PeriodEndUtc), Utc(history.Select(p => (DateTime?)p.PeriodEndUtc).Max()), current.Length > 0,
                Aggregate(rows), userIds.Select(userId => new UserRow(userId,
                    userId != null && users.TryGetValue(userId.Value, out var user)
                        ? string.Join(" ", new[] { user.FirstName, user.LastName }.Where(s => !string.IsNullOrWhiteSpace(s))) is { Length: > 0 } name ? name : user.Email
                        : "Utilisateur non attribué / supprimé",
                    AggregateUser(usageByUser.SingleOrDefault(group => group.Key == userId)?.ToArray() ?? []))).ToArray());
        }).ToArray();
        var wallet = await db.CompanyWallets.AsNoTracking().SingleOrDefaultAsync(w => w.CompanyId == companyId, ct);
        return new Report(company.Name, wallet?.Balance ?? 0, Aggregate(usages), result);
    }
}
