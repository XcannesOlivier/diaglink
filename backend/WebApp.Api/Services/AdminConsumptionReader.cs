using Microsoft.EntityFrameworkCore;
using WebApp.Api.Data;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;

namespace WebApp.Api.Services;

/// <summary>Read-only usage valuation; never persists or replays billing.</summary>
public static class AdminConsumptionReader
{
    public record Metrics(int Responses, int Vision, int Summaries, long Input, long Output, long Tokens,
        int Unknown, int Unvalued, decimal RealCost, decimal CommercialCredit, decimal WalletRealCost,
        object[] Providers);
    public record UserMetrics(int Responses, int Vision, int Summaries, long Input, long Output, long Tokens,
        int Unknown, int Unvalued, decimal RealCost, decimal CommercialCredit, decimal WalletRealCost,
        object[] Providers, decimal IncludedQuotaConsumed);
    public record UserRow(Guid? Id, string Name, UserMetrics Metrics);
    public record MachineRow(Guid? Id, string Name, bool Billable, decimal Budget, decimal Used,
        decimal Remaining, DateTime? ResetUtc, DateTime? RightsEndUtc, bool HasPaidRights,
        Metrics Metrics, UserRow[] Users);
    public record Report(string CompanyName, decimal WalletBalance, Metrics Metrics, MachineRow[] Machines);

    public static async Task<IResult> ReadAsync(Guid companyId, string? from, string? to, string? usageType,
        DiagLinkDbContext db, CancellationToken ct)
    {
        if (!AiUsageFilter.TryParse(from, to, usageType, out var filter)) return Results.BadRequest();
        var report = await ReadReportAsync(companyId, filter, db, ct);
        return report == null ? Results.NotFound() : Results.Ok(report);
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
        var costs = new AiCostCalculator(db);
        var converter = new AiCostCurrencyConverter(db);
        var valued = new Dictionary<Guid, decimal?>();
        foreach (var usage in usages)
        {
            ct.ThrowIfCancellationRequested();
            var cost = await costs.CalculateAsync(usage, ct);
            decimal? eur = null;
            if (cost.IsValuable && cost.RealAiCost != null)
            {
                var converted = await converter.ConvertAsync(cost.RealAiCost.Value, cost.Currency, usage, ct);
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
            rows.Count(u => !u.Available || u.InputTokens == null || u.OutputTokens == null),
            rows.Count(u => valued[u.Id] == null), rows.Sum(u => valued[u.Id] ?? 0),
            rows.Sum(u => walletDebits[u.Id].Sum(e => e.CommercialCreditAmount ?? 0)),
            rows.Sum(u => walletDebits[u.Id].Sum(e => e.RealAiCost ?? 0)),
            rows.GroupBy(u => new { u.Provider, u.Model }).Select(g => (object)new { g.Key.Provider, g.Key.Model, Count = g.Count() }).ToArray());
        UserMetrics AggregateUser(AiUsageRecord[] rows)
        {
            var metrics = Aggregate(rows);
            return new(metrics.Responses, metrics.Vision, metrics.Summaries, metrics.Input, metrics.Output, metrics.Tokens,
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
