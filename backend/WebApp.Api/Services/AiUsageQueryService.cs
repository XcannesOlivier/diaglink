using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using WebApp.Api.Data;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;

namespace WebApp.Api.Services;

public class AiUsageQueryService(DiagLinkDbContext db)
{
    public class Aggregate
    {
        public Guid? Id { get; set; }
        public long Events { get; set; }
        public long Chats { get; set; }
        public long Summaries { get; set; }
        public long Visions { get; set; }
        public long Known { get; set; }
        public long Completed { get; set; }
        public long? Input { get; set; }
        public long? Output { get; set; }
        public long? Total { get; set; }
        public long Machines { get; set; }
        public long Users { get; set; }
        public long NoCompany { get; set; }
        public long NoMachine { get; set; }
        public long NoUser { get; set; }
        public AiUsageMetricsDto Metrics() => new(Events, Chats, Summaries, Visions, Known, Events - Known,
            Completed, Events - Completed, Events == 0 ? 0 : Known == 0 ? null : Input,
            Events == 0 ? 0 : Known == 0 ? null : Output, Events == 0 ? 0 : Known == 0 ? null : Total);
    }

    private IQueryable<AiUsageRecord> Records(AiUsageFilter filter)
    {
        var q = db.AiUsageRecords.AsNoTracking().Where(r => r.CreatedAtUtc < filter.To.UtcDateTime);
        if (filter.From.HasValue) q = q.Where(r => r.CreatedAtUtc >= filter.From.Value.UtcDateTime);
        if (filter.UsageType.HasValue) q = q.Where(r => r.UsageType == filter.UsageType.Value);
        return q;
    }

    private static IQueryable<Aggregate> AggregateBy(IQueryable<AiUsageRecord> records,
        Expression<Func<AiUsageRecord, Guid?>> key) => records.GroupBy(key).Select(g => new Aggregate {
            Id = g.Key, Events = g.LongCount(),
            Chats = g.LongCount(r => r.UsageType == AiUsageType.ChatResponse),
            Visions = g.LongCount(r => r.UsageType == AiUsageType.VisionTool),
            Summaries = g.LongCount(r => r.UsageType == AiUsageType.ConversationSummary),
            Known = g.LongCount(r => r.Available), Completed = g.LongCount(r => r.Completed),
            Input = g.Sum(r => r.Available ? (long?)r.InputTokens : null),
            Output = g.Sum(r => r.Available ? (long?)r.OutputTokens : null),
            Total = g.Sum(r => r.Available ? (long?)r.TotalTokens : null),
            Machines = g.Where(r => r.MachineId != null).Select(r => r.MachineId).Distinct().LongCount(),
            Users = g.Where(r => r.UserId != null).Select(r => r.UserId).Distinct().LongCount(),
            NoCompany = g.LongCount(r => r.CompanyId == null),
            NoMachine = g.LongCount(r => r.MachineId == null),
            NoUser = g.LongCount(r => r.UserId == null)
        });

    public async Task<AiUsageSummaryDto> SummaryAsync(AiUsageFilter f, CancellationToken ct)
    {
        var a = await AggregateBy(Records(f), r => (Guid?)null).SingleOrDefaultAsync(ct) ?? new Aggregate();
        return new(f.From, f.To, f.UsageType?.ToString(), a.Metrics(), a.NoCompany, a.NoMachine, a.NoUser);
    }
    public async Task<AiUsageSummaryDto> CompanySummaryAsync(AiUsageFilter f, Guid companyId, CancellationToken ct)
    {
        var a = await AggregateBy(Records(f).Where(r => r.CompanyId == companyId), r => (Guid?)null)
            .SingleOrDefaultAsync(ct) ?? new Aggregate();
        return new(f.From, f.To, f.UsageType?.ToString(), a.Metrics(), a.NoCompany, a.NoMachine, a.NoUser);
    }
    public async Task<List<CompanyUsageDto>> CompaniesAsync(AiUsageFilter f, CancellationToken ct)
    {
        var rows = await (from a in AggregateBy(Records(f), r => r.CompanyId)
            join c in db.Companies.AsNoTracking() on a.Id equals (Guid?)c.Id into companies
            from c in companies.DefaultIfEmpty()
            select new { A = a, Name = c == null ? null : c.Name }).ToListAsync(ct);
        return rows.Select(r => new CompanyUsageDto(r.A.Id,
            r.A.Id == null ? "Entreprise non attribuée" : r.Name ?? "Entreprise introuvable",
            r.A.Machines, r.A.Users, r.A.Metrics())).OrderBy(r => r.CompanyName).ThenBy(r => r.CompanyId).ToList();
    }
    public async Task<List<MachineUsageDto>> MachinesAsync(AiUsageFilter f, Guid? companyId, CancellationToken ct)
    {
        var rows = await (from a in AggregateBy(Records(f).Where(r => r.CompanyId == companyId), r => r.MachineId)
            join m in db.Machines.AsNoTracking() on a.Id equals (Guid?)m.Id into machines
            from m in machines.DefaultIfEmpty()
            select new { A = a, Name = m == null ? null : m.Name }).ToListAsync(ct);
        return rows.Select(r => new MachineUsageDto(r.A.Id,
            r.A.Id == null ? "Machine non attribuée" : r.Name ?? "Machine introuvable",
            r.A.Users, r.A.Metrics())).OrderBy(r => r.MachineName).ThenBy(r => r.MachineId).ToList();
    }
    public async Task<List<UserUsageDto>> UsersAsync(AiUsageFilter f, Guid? companyId, Guid? machineId, CancellationToken ct)
    {
        var rows = await (from a in AggregateBy(Records(f).Where(r => r.CompanyId == companyId && r.MachineId == machineId), r => r.UserId)
            join u in db.Users.AsNoTracking() on a.Id equals (Guid?)u.Id into users
            from u in users.DefaultIfEmpty()
            select new { A = a, First = u == null ? null : u.FirstName,
                Last = u == null ? null : u.LastName, Email = u == null ? null : u.Email }).ToListAsync(ct);
        return rows.Select(r => {
            var name = string.Join(" ", new[] { r.First, r.Last }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()));
            return new UserUsageDto(r.A.Id, r.A.Id == null ? "Utilisateur non attribué" :
                name.Length > 0 ? name : !string.IsNullOrWhiteSpace(r.Email) ? r.Email : "Utilisateur introuvable",
                r.Email, r.A.Metrics());
        }).OrderBy(r => r.UserDisplayName).ThenBy(r => r.UserId).ToList();
    }
}
