using System.Data;
using Microsoft.EntityFrameworkCore;
using WebApp.Api.Data;
using WebApp.Api.Models.Entities;
namespace WebApp.Api.Services;

public sealed class StripeMachineStatusService(DbContextOptions<DiagLinkDbContext> options, IStripeLifecycleGateway gateway,
    StripeMachineAdditionService additions)
{
    public async Task<string> SetActiveAsync(Guid companyId, Guid machineId, bool active, Guid requestId, CancellationToken ct)
    {
        if(requestId==Guid.Empty) throw new InvalidOperationException("Request id required");
        var key=$"machine-status:{requestId:N}"; var kind=active?"MachineActivated":"MachineDeactivated";
        await using var strategy=new DiagLinkDbContext(options);
        var operation=await strategy.Database.CreateExecutionStrategy().ExecuteAsync(async()=>
        {
            await using var db=new DiagLinkDbContext(options); await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
            var companies=db.Database.IsSqlServer()?db.Companies.FromSqlInterpolated($"SELECT * FROM dbo.Companies WITH (UPDLOCK,HOLDLOCK) WHERE Id={companyId}"):db.Companies.AsQueryable();
            await companies.SingleAsync(c=>c.Id==companyId,ct);
            var previous=await db.StripeLifecycleEvents.SingleOrDefaultAsync(e=>e.Id==key,ct);
            if(previous!=null)
            {
                if(previous.CompanyId!=companyId || previous.MachineId!=machineId || previous.EventType!=kind) throw new InvalidOperationException("Conflicting request");
                return previous;
            }
            if(await db.StripeMachineAdditions.AnyAsync(a=>a.CompanyId==companyId && a.CompletedAtUtc==null,ct)
                || await db.StripeLifecycleEvents.AnyAsync(e=>e.CompanyId==companyId && e.CompletedAtUtc==null,ct)) throw new InvalidOperationException("Finish pending operation first");
            var machine=await db.Machines.SingleAsync(m=>m.Id==machineId && m.CompanyId==companyId,ct);
            var account=await db.BillingAccounts.SingleAsync(a=>a.CompanyId==companyId,ct);
            var state=await gateway.ReadAsync(account,ct);
            var now=DateTime.UtcNow;
            var covered=await db.MachineBillingPeriods.AnyAsync(p=>p.MachineId==machineId && p.PeriodStartUtc<=now && now<p.PeriodEndUtc,ct);
            if(active && !covered && (state.Status!="active" || state.CancelAtPeriodEnd)) throw new InvalidOperationException("Active non-canceling subscription required");
            machine.Status=active?"active":"inactive"; machine.UpdatedAtUtc=now;
            await db.SaveChangesAsync(ct);
            // Lowering a quantity or restoring already paid rights only changes the next invoice.
            if(!active || covered)
            {
                var count=await db.Machines.CountAsync(m=>m.CompanyId==companyId && m.Status=="active",ct);
                await gateway.SetQuantityAsync(account,state,count,key,ct);
            }
            var op=new StripeLifecycleEvent {Id=key,CompanyId=companyId,MachineId=machineId,SubscriptionId=account.StripeSubscriptionId!,EventType=kind,
                CreatedAtUtc=now,CompletedAtUtc=!active || covered?now:null};
            db.StripeLifecycleEvents.Add(op); await db.SaveChangesAsync(ct);await tx.CommitAsync(ct); return op;
        });
        if(operation.CompletedAtUtc!=null) return "Synchronized";
        var result=await additions.AddActiveMachineAsync(machineId,DateTime.SpecifyKind(operation.CreatedAtUtc,DateTimeKind.Utc),ct);
        if(result.Status=="ReconciliationRequired") throw new InvalidOperationException("Addition requires reconciliation");
        await using(var db=new DiagLinkDbContext(options))
        {
            var op=await db.StripeLifecycleEvents.SingleAsync(e=>e.Id==key,ct);op.CompletedAtUtc=DateTime.UtcNow; await db.SaveChangesAsync(ct);
        }
        return result.Status;
    }
}
