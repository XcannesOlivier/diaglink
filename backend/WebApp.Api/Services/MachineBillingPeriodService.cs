using System.Data;
using Microsoft.EntityFrameworkCore;
using WebApp.Api.Data;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;

namespace WebApp.Api.Services;

/// <summary>Creates included budgets from authoritative external cycle boundaries; never computes an anchor.</summary>
public sealed class MachineBillingPeriodService(DbContextOptions<DiagLinkDbContext> options)
{
    public Task<MachineBillingPeriodResult> CreateInitialPeriodAsync(Guid machineId, DateTime machineActivationUtc,
        DateTime cycleStartUtc, DateTime cycleEndUtc, CancellationToken ct = default, Guid? expectedCompanyId = null) =>
        CreateAsync(machineId, machineActivationUtc, cycleStartUtc, cycleEndUtc, ct, expectedCompanyId);

    public Task<MachineBillingPeriodResult> RenewPeriodAsync(Guid machineId, DateTime cycleStartUtc,
        DateTime cycleEndUtc, CancellationToken ct = default, Guid? expectedCompanyId = null) => CreateAsync(machineId, null, cycleStartUtc, cycleEndUtc, ct, expectedCompanyId);

    private async Task<MachineBillingPeriodResult> CreateAsync(Guid machineId, DateTime? activation,
        DateTime start, DateTime end, CancellationToken ct, Guid? expectedCompanyId)
    {
        await using var strategyContext = new DiagLinkDbContext(options);
        var strategy = strategyContext.Database.CreateExecutionStrategy();
        // Closing and creating periods are replayed together with fresh tracked entities.
        return await strategy.ExecuteAsync(token => CreateAttemptAsync(machineId, activation, start, end, token, expectedCompanyId), ct);
    }

    private async Task<MachineBillingPeriodResult> CreateAttemptAsync(Guid machineId, DateTime? activation,
        DateTime start, DateTime end, CancellationToken ct, Guid? expectedCompanyId)
    {
        ct.ThrowIfCancellationRequested();
        MachineBillingPeriodResult Fail(string reason) => new() { MachineId=machineId, Status=reason, FailureReason=reason };
        if (start.Kind == DateTimeKind.Local || end.Kind == DateTimeKind.Local || activation?.Kind == DateTimeKind.Local || end <= start)
            return Fail("InvalidCycleDates");
        if (activation >= end) return Fail("ActivationOutsideCycle");
        var periodStart = activation.HasValue && activation.Value > start ? activation.Value : start;
        await using var db = new DiagLinkDbContext(options);
        if (!db.Database.IsSqlServer() && db.Database.ProviderName != "Microsoft.EntityFrameworkCore.Sqlite")
            return Fail("UnsupportedDatabaseProvider");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var machines = db.Database.IsSqlServer()
            ? db.Machines.FromSqlInterpolated($"SELECT * FROM [dbo].[Machines] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {machineId}")
            : db.Machines.AsQueryable();
        var machine = await machines.SingleOrDefaultAsync(m => m.Id == machineId, ct);
        if (machine == null) return Fail("MachineNotFound");
        if (expectedCompanyId.HasValue && machine.CompanyId != expectedCompanyId.Value) return Fail("MachineCompanyMismatch");
        if (machine.Status != "active") return Fail("MachineInactive");
        var periods = await db.MachineBillingPeriods.Where(p => p.MachineId == machineId).OrderBy(p => p.PeriodStartUtc).ToListAsync(ct);
        var sameStart = periods.Where(p => p.PeriodStartUtc == periodStart).ToList();
        if (sameStart.Count > 1 || sameStart.Any(p => p.PeriodEndUtc != end)) return Fail("DataInconsistency");
        if (periods.Any(p => p.PeriodStartUtc != periodStart && p.PeriodStartUtc < end && periodStart < p.PeriodEndUtc))
            return Fail("OverlappingPeriod");
        if (sameStart.Count == 1) return Result(sameStart[0], "AlreadyExists");
        if (activation.HasValue && periods.Count != 0) return Fail("InitialPeriodAlreadyExists");
        if (!activation.HasValue)
        {
            if (periods.Count == 0) return Fail("PreviousPeriodNotFound");
            var previous = periods[^1];
            if (previous.PeriodEndUtc > periodStart || periods.Any(p => p.PeriodEndUtc <= p.PeriodStartUtc))
                return Fail("DataInconsistency");
            if (previous.Status is not ("Active" or "Closed") || periods.Take(periods.Count-1).Any(p => p.Status != "Closed"))
                return Fail("DataInconsistency");
            if (previous.Status == "Active")
            {
                previous.Status = "Closed";
                previous.UpdatedAtUtc = DateTime.UtcNow;
            }
        }
        var now = DateTime.UtcNow;
        var created = new MachineBillingPeriod { Id=Guid.NewGuid(), MachineId=machineId,
            PeriodStartUtc=periodStart, PeriodEndUtc=end, IncludedAiBudgetRealCost=10m,
            IncludedAiUsedRealCost=0m, Status="Active", CreatedAtUtc=now, UpdatedAtUtc=now };
        db.MachineBillingPeriods.Add(created);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Result(created, "Created");
    }

    private static MachineBillingPeriodResult Result(MachineBillingPeriod period, string status) => new()
    {
        Success=true, Status=status, MachineId=period.MachineId, BillingPeriodId=period.Id,
        PeriodStartUtc=period.PeriodStartUtc, PeriodEndUtc=period.PeriodEndUtc,
        IncludedAiBudgetRealCost=period.IncludedAiBudgetRealCost, IncludedAiUsedRealCost=period.IncludedAiUsedRealCost
    };
}
