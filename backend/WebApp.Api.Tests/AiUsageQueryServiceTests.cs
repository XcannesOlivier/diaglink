using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Data;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

[TestClass]
public class AiUsageQueryServiceTests
{
    private static readonly DateTimeOffset End = new(2026, 9, 9, 0, 0, 0, TimeSpan.Zero);
    private static AiUsageFilter Filter => new(End.AddDays(-1), End, null);
    private static DiagLinkDbContext Db() => new(new DbContextOptionsBuilder<DiagLinkDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static AiUsageRecord Record(Guid? company = null, Guid? machine = null, Guid? user = null,
        bool available = true, AiUsageType type = AiUsageType.ChatResponse) => new() {
        Id = Guid.NewGuid(), CompanyId = company, MachineId = machine, UserId = user, UsageType = type,
        Available = available, Completed = available, InputTokens = available ? 100 : null,
        OutputTokens = available ? 20 : null, TotalTokens = available ? 120 : null,
        CreatedAtUtc = End.AddHours(-1).UtcDateTime
    };

    [TestMethod]
    public async Task VisionFilterAndAllUsageAggregateAcrossEveryLevelWithCompanyIsolation()
    {
        await using var db = Db();
        var company = Guid.NewGuid(); var machine = Guid.NewGuid(); var user = Guid.NewGuid();
        db.AddRange(Record(company, machine, user),
            Record(company, machine, user, type: AiUsageType.ConversationSummary),
            Record(company, machine, user, type: AiUsageType.VisionTool),
            Record(company, machine, user, available: false, type: AiUsageType.VisionTool),
            Record(Guid.NewGuid(), type: AiUsageType.VisionTool));
        await db.SaveChangesAsync();
        var service = new AiUsageQueryService(db);
        var all = (await service.SummaryAsync(Filter, default)).Metrics;
        Assert.AreEqual(5L, all.EventCount); Assert.AreEqual(3L, all.VisionToolCount);
        Assert.AreEqual(1L, all.ChatResponseCount); Assert.AreEqual(1L, all.ConversationSummaryCount);
        Assert.AreEqual(480L, all.TotalTokens); Assert.AreEqual(400L, all.InputTokens);
        Assert.AreEqual(80L, all.OutputTokens); Assert.AreEqual(1L, all.UnknownUsageCount);
        Assert.IsTrue(AiUsageFilter.TryParse(null, End.ToString("O"), "VisionTool", out var vision));
        foreach (var filter in new[] { Filter, vision })
        {
            var expected = filter.UsageType == null ? 360L : 120L;
            var metrics = new[] {
                (await service.CompanySummaryAsync(filter, company, default)).Metrics,
                (await service.CompaniesAsync(filter, default)).Single(c => c.CompanyId == company).Metrics,
                (await service.MachinesAsync(filter, company, default)).Single().Metrics,
                (await service.UsersAsync(filter, company, machine, default)).Single().Metrics
            };
            foreach (var m in metrics)
            {
                Assert.AreEqual(2L, m.VisionToolCount); Assert.AreEqual(expected, m.TotalTokens);
                Assert.AreEqual(1L, m.UnknownUsageCount);
                Assert.AreEqual(filter.UsageType == null ? 1L : 0L, m.ChatResponseCount);
                Assert.AreEqual(filter.UsageType == null ? 1L : 0L, m.ConversationSummaryCount);
            }
        }
    }

    [TestMethod]
    public async Task EmptyAndUnknownAreDifferent()
    {
        await using var db = Db(); var service = new AiUsageQueryService(db);
        var empty = await service.SummaryAsync(Filter, default);
        Assert.AreEqual(0L, empty.Metrics.TotalTokens);
        db.AiUsageRecords.Add(Record(available: false)); await db.SaveChangesAsync();
        var unknown = await service.SummaryAsync(Filter, default);
        Assert.IsNull(unknown.Metrics.TotalTokens);
        Assert.AreEqual(1L, unknown.Metrics.UnknownUsageCount);
        Assert.AreEqual(1L, unknown.Metrics.NotCompletedCount);
        Assert.AreEqual(1L, unknown.UnassignedCompanyCount);
    }

    [TestMethod]
    public async Task SummaryExposesCacheTokenCategoriesAndTreatsLegacyNullsAsZero()
    {
        await using var db = Db();
        var cached = Record();
        cached.CacheReadInputTokens = 10;
        cached.CacheCreationInputTokens = 50;
        cached.CacheCreation5mInputTokens = 20;
        cached.CacheCreation1hInputTokens = 30;
        db.AddRange(cached, Record());
        await db.SaveChangesAsync();

        var metrics = (await new AiUsageQueryService(db).SummaryAsync(Filter, default)).Metrics;

        Assert.AreEqual(10L, metrics.CacheReadInputTokens);
        Assert.AreEqual(50L, metrics.CacheCreationInputTokens);
        Assert.AreEqual(20L, metrics.CacheCreation5mInputTokens);
        Assert.AreEqual(30L, metrics.CacheCreation1hInputTokens);
    }

    [TestMethod]
    public async Task MixedUsageAndHalfOpenPeriodAndType()
    {
        await using var db = Db(); var service = new AiUsageQueryService(db);
        var knownIncomplete = Record(); knownIncomplete.Completed = false;
        var outside = Record(); outside.CreatedAtUtc = End.UtcDateTime;
        var atStart = Record(type: AiUsageType.ConversationSummary); atStart.CreatedAtUtc = Filter.From!.Value.UtcDateTime;
        db.AddRange(knownIncomplete, atStart, Record(available: false), outside); await db.SaveChangesAsync();
        var all = await service.SummaryAsync(Filter, default);
        Assert.AreEqual(3L, all.Metrics.EventCount);
        Assert.AreEqual(240L, all.Metrics.TotalTokens);
        Assert.AreEqual(2L, all.Metrics.ChatResponseCount);
        Assert.AreEqual(1L, all.Metrics.ConversationSummaryCount);
        Assert.AreEqual(2L, all.Metrics.NotCompletedCount);
        var summary = await service.SummaryAsync(Filter with { UsageType = AiUsageType.ConversationSummary }, default);
        Assert.AreEqual(1L, summary.Metrics.EventCount);
        Assert.AreEqual(120L, summary.Metrics.TotalTokens);
    }

    [TestMethod]
    public async Task HistoricalGroupsDistinctCountsAndMissingEntities()
    {
        await using var db = Db(); var service = new AiUsageQueryService(db);
        var company = Guid.NewGuid(); var machine = Guid.NewGuid(); var user = Guid.NewGuid();
        db.Companies.Add(new Company { Id = company, Name = "Client", Status = "active" });
        db.Machines.Add(new Machine { Id = machine, CompanyId = Guid.NewGuid(), Name = "Machine", Status = "active" });
        db.Users.Add(new User { Id = user, CompanyId = Guid.NewGuid(), Email = "admin@example.test", FirstName = " ",
            LastName = null, Role = "diaglink_super_admin", Status = "active" });
        db.AddRange(Record(company, machine, user), Record(company, machine, user),
            Record(company, null, null, false), Record(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), Record());
        await db.SaveChangesAsync();
        var companies = await service.CompaniesAsync(Filter, default);
        var client = companies.Single(r => r.CompanyId == company);
        Assert.AreEqual(1L, client.MachineCountUsed); Assert.AreEqual(1L, client.UserCountUsed);
        Assert.AreEqual(3L, client.Metrics.EventCount);
        Assert.IsTrue(companies.Any(r => r.CompanyName == "Entreprise introuvable" && r.CompanyId != null));
        Assert.IsTrue(companies.Any(r => r.CompanyName == "Entreprise non attribuée" && r.CompanyId == null));
        var machines = await service.MachinesAsync(Filter, company, default);
        Assert.AreEqual("Machine", machines.Single(r => r.MachineId == machine).MachineName);
        Assert.IsNull(machines.Single(r => r.MachineId == null).Metrics.TotalTokens);
        var users = await service.UsersAsync(Filter, company, machine, default);
        Assert.AreEqual("admin@example.test", users.Single().UserDisplayName);
        Assert.AreEqual(2L, users.Single().Metrics.EventCount);
        Assert.AreEqual(0, (await service.UsersAsync(Filter, Guid.NewGuid(), machine, default)).Count);
        Assert.AreEqual("Utilisateur non attribué", (await service.UsersAsync(Filter, company, null, default)).Single().UserDisplayName);
        var missing = companies.Single(r => r.CompanyName == "Entreprise introuvable");
        var missingMachine = (await service.MachinesAsync(Filter, missing.CompanyId, default)).Single();
        Assert.AreEqual("Machine introuvable", missingMachine.MachineName);
        Assert.AreEqual("Utilisateur introuvable", (await service.UsersAsync(Filter, missing.CompanyId, missingMachine.MachineId, default)).Single().UserDisplayName);
    }

    [TestMethod]
    public async Task CompanyScopeIsolatesUsageAndMachineAccessRejectsOtherCompany()
    {
        await using var db = Db();
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var machineA = Guid.NewGuid(); var machineB = Guid.NewGuid();
        db.Machines.AddRange(
            new Machine { Id = machineA, CompanyId = a, Name = "A", Status = "active" },
            new Machine { Id = machineB, CompanyId = b, Name = "B", Status = "active" });
        db.AddRange(Record(a, machineA), Record(b, machineB), Record(),
            Record(a, machineA, type: AiUsageType.ConversationSummary));
        await db.SaveChangesAsync();
        var service = new AiUsageQueryService(db);
        var summary = await service.CompanySummaryAsync(Filter, a, default);
        Assert.AreEqual(2L, summary.Metrics.EventCount);
        Assert.AreEqual(240L, summary.Metrics.TotalTokens);
        Assert.AreEqual(0L, summary.UnassignedCompanyCount);
        Assert.AreEqual(1L, (await service.CompanySummaryAsync(Filter with {
            UsageType = AiUsageType.ConversationSummary }, a, default)).Metrics.EventCount);
        Assert.AreEqual(0L, (await service.CompanySummaryAsync(Filter with {
            From = End }, a, default)).Metrics.EventCount);
        Assert.AreEqual(machineA, (await service.MachinesAsync(Filter, a, default)).Single().MachineId);
        Assert.AreEqual(0, (await service.UsersAsync(Filter, a, machineB, default)).Count);
        var principal = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[] {
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, DiagLinkRoles.CompanyAdmin),
            new System.Security.Claims.Claim(DiagLinkClaimTypes.CompanyId, a.ToString())
        }, "test"));
        var access = new MachineAccessService(db);
        Assert.IsTrue(await access.CanAccessMachineAsync(principal, machineA, default));
        Assert.IsFalse(await access.CanAccessMachineAsync(principal, machineB, default));
    }

    [TestMethod]
    public void AggregatesAndLeftJoinsTranslateWithSqlServer()
    {
        using var db = new DiagLinkDbContext(new DbContextOptionsBuilder<DiagLinkDbContext>()
            .UseSqlServer("Server=localhost;Database=translation_only;Integrated Security=true;TrustServerCertificate=true").Options);
        var method = typeof(AiUsageQueryService).GetMethod("AggregateBy",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        foreach (System.Linq.Expressions.Expression<Func<AiUsageRecord, Guid?>> key in
            new System.Linq.Expressions.Expression<Func<AiUsageRecord, Guid?>>[] {
                r => (Guid?)null, r => r.CompanyId, r => r.MachineId, r => r.UserId })
        {
            var aggregate = (IQueryable<AiUsageQueryService.Aggregate>)method.Invoke(null,
                new object[] { db.AiUsageRecords.AsNoTracking().Where(r => r.CreatedAtUtc < End.UtcDateTime), key })!;
            var query = from a in aggregate
                join u in db.Users on a.Id equals (Guid?)u.Id into users
                from u in users.DefaultIfEmpty()
                select new { A = a, Name = u == null ? null : u.Email };
            var sql = query.ToQueryString();
            StringAssert.Contains(sql, "GROUP BY");
            StringAssert.Contains(sql, "LEFT JOIN");
            StringAssert.Contains(sql, "COUNT_BIG");
            StringAssert.Contains(sql, "DISTINCT");
            StringAssert.Contains(sql, "bigint");
        }
    }

    [TestMethod]
    public void ValidatesDatesTypesAndUnassignedScope()
    {
        Assert.IsTrue(AiUsageFilter.TryParse("2026-09-08T00:00:00+02:00", "2026-09-09T00:00:00Z", "ChatResponse", out var filter));
        Assert.AreEqual(22, filter.From!.Value.Hour);
        Assert.IsFalse(AiUsageFilter.TryParse("2026-09-08", null, null, out _));
        Assert.IsFalse(AiUsageFilter.TryParse(null, null, "0", out _));
        Assert.IsFalse(AiUsageFilter.TryParse("2026-09-09T00:00:00Z", "2026-09-08T00:00:00Z", null, out _));
        Assert.IsTrue(AiUsageFilter.TryScope("unassigned", out var id)); Assert.IsNull(id);
        Assert.IsFalse(AiUsageFilter.TryScope(null, out _));
    }
}
