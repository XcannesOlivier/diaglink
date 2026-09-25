using Microsoft.EntityFrameworkCore;
using WebApp.Api.Data;

namespace WebApp.Api.Services;

public record StripeCompanySummary(string? StripeCustomerId, string? StripeSubscriptionId,
    string? SubscriptionStatus, DateTime? CurrentPeriodStartUtc, DateTime? CurrentPeriodEndUtc,
    int ActiveMachineCount, bool TestActionsEnabled)
{
    public Guid? BillingAccountId { get; init; }
    public IReadOnlyList<StripeActiveMachine> ActiveMachines { get; init; } = [];
    public IReadOnlyList<StripeMachineBillingState> Machines { get; init; } = [];
    public bool CancelAtPeriodEnd { get; init; }
    public string? LatestInvoiceId { get; init; }
    public string? LatestInvoiceStatus { get; init; }
    public long? AmountRemainingCents { get; init; }
    public bool MachineRequestProvisioningCompleted { get; init; }
}

public record StripeMachineBillingState(Guid Id, string Name, bool Billable, DateTime? RightsEndUtc);

public record StripeActiveMachine(Guid Id, string Name, bool HasBillingPeriod);
public record StripeAdditionSummary(Guid Id, Guid MachineId, string MachineName, string Stage,
    string? StripeInvoiceId, long TargetQuantity, decimal AiAmountEur, decimal ServiceAmountEur,
    DateTime ActivatedAtUtc, DateTime CycleStartUtc, DateTime CycleEndUtc, DateTime? PaymentConfirmedAtUtc,
    Guid? MachineBillingPeriodId, string? ExternalEventId, DateTime? CompletedAtUtc, bool ReconciliationRequired);

public static class StripeAdminEndpoints
{
    public static void MapStripeAdminEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/admin/finance/topups-total",AdminFinanceReader.GlobalTopUpsAsync).RequireAuthorization("SuperAdminOnly");
        app.MapGet("/api/admin/finance/period",AdminFinanceReader.PeriodFinanceAsync).RequireAuthorization("SuperAdminOnly");
        var group = app.MapGroup("/api/companies/{companyId:guid}/stripe").RequireAuthorization("SuperAdminOnly");
        group.MapGet("", ReadAsync);
        group.MapGet("/finance", AdminFinanceReader.OverviewAsync);
        group.MapGet("/finance/technical", AdminFinanceReader.TechnicalAsync);
        group.MapGet("/finance/consumption", AdminConsumptionReader.ReadAsync);
        group.MapGet("/machine-additions", ReadAdditionsAsync);
        group.MapGet("/subscription-payment", ReadSubscriptionPaymentAsync);
        group.MapPost("/machines/{machineId:guid}/status", SetMachineStatusAsync);
        group.MapPost("/machine-additions/{machineId:guid}", AddMachineAsync);
        group.MapPost("/customer", (Guid companyId, DiagLinkDbContext db, StripeBillingService billing,
            StripeBillingOptions settings, CancellationToken ct) => WriteAsync(companyId, false, db, billing, settings, ct));
        group.MapPost("/subscription", (Guid companyId, DiagLinkDbContext db, StripeBillingService billing,
            StripeBillingOptions settings, CancellationToken ct) => WriteAsync(companyId, true, db, billing, settings, ct));
    }

    public static bool TestActionsEnabled(StripeBillingOptions settings)
    {
        try { settings.Validate(); }
        catch (InvalidOperationException) { return false; }
        return settings.SecretKey.StartsWith("sk_test_", StringComparison.Ordinal)
            || settings.SecretKey.StartsWith("rk_test_", StringComparison.Ordinal);
    }

    public record MachineStatusRequest(bool Active, Guid RequestId);
    public static async Task<IResult> SetMachineStatusAsync(Guid companyId, Guid machineId, MachineStatusRequest request,
        [Microsoft.AspNetCore.Mvc.FromServices] StripeMachineStatusService service, StripeBillingOptions settings, CancellationToken ct)
    {
        if(!TestActionsEnabled(settings)) return Results.Conflict(new {error="Stripe test requis."});
        try { return Results.Ok(new {status=await service.SetActiveAsync(companyId,machineId,request.Active,request.RequestId,ct)}); }
        catch(InvalidOperationException) {return Results.Conflict(new {error="Vérifiez l’abonnement et terminez toute opération en cours avant de réessayer."});}
        catch(Stripe.StripeException) {return Results.Json(new {error="Synchronisation Stripe non confirmée : reprendre la même requête."},statusCode:502);}
    }

    public static async Task<IResult> ReadSubscriptionPaymentAsync(Guid companyId, DiagLinkDbContext db,
        [Microsoft.AspNetCore.Mvc.FromServices] IStripeSubscriptionPaymentGateway gateway, StripeBillingOptions settings, CancellationToken ct)
    {
        if (!TestActionsEnabled(settings)) return Results.Conflict(new { error = "Stripe test requis." });
        if (!await db.Companies.AnyAsync(c => c.Id == companyId, ct)) return Results.NotFound();
        var account = await db.BillingAccounts.AsNoTracking().SingleOrDefaultAsync(a => a.CompanyId == companyId, ct);
        var payments = await db.StripeSubscriptionPayments.AsNoTracking().Where(p => p.CompanyId == companyId)
            .OrderByDescending(p => p.PeriodStartUtc).Select(p => new
            {
                p.StripeInvoiceId, p.BillingReason, p.PeriodStartUtc, p.PeriodEndUtc, p.AmountPaidCents,
                p.PaymentConfirmedAtUtc, p.CompletedAtUtc, Status = p.CompletedAtUtc == null ? "PaymentConfirmed" : "Completed"
            }).Take(12).ToListAsync(ct);
        try
        {
            var invoice = account?.StripeSubscriptionId == null ? null : await gateway.ReadAsync(account, null, ct);
            return Results.Ok(new { Invoice = invoice, Payments = payments });
        }
        catch (InvalidOperationException) { return Results.Conflict(new { error = "Facture à vérifier : réconciliation requise." }); }
        catch (Stripe.StripeException) { return Results.Json(new { error = "Lecture Stripe indisponible." }, statusCode: 502); }
    }

    public static async Task<IResult> ReadAsync(Guid companyId, DiagLinkDbContext db, StripeBillingOptions settings, CancellationToken ct)
    {
        if (!await db.Companies.AnyAsync(c => c.Id == companyId, ct)) return Results.NotFound();
        var account = await db.BillingAccounts.AsNoTracking().SingleOrDefaultAsync(a => a.CompanyId == companyId, ct);
        var machines = await db.Machines.AsNoTracking().Where(m => m.CompanyId == companyId && m.Status == "active")
            .OrderBy(m => m.Name).Select(m => new StripeActiveMachine(m.Id, m.Name,
                db.MachineBillingPeriods.Any(p => p.MachineId == m.Id && p.PeriodStartUtc <= DateTime.UtcNow && p.PeriodEndUtc > DateTime.UtcNow))).ToListAsync(ct);
        var machineStates = await db.Machines.AsNoTracking().Where(m => m.CompanyId == companyId).OrderBy(m=>m.Name)
            .Select(m=>new StripeMachineBillingState(m.Id,m.Name,m.Status=="active",
                db.MachineBillingPeriods.Where(p=>p.MachineId==m.Id).Max(p=>(DateTime?)p.PeriodEndUtc))).ToListAsync(ct);
        var machineRequestProvisioningCompleted = await db.MachineRequestPayments.AsNoTracking().AnyAsync(payment =>
            payment.CompanyId == companyId
            && payment.ProvisioningStage == Models.Entities.MachineRequestProvisioningStage.Completed
            && payment.ProvisioningCompletedAtUtc != null, ct);
        static DateTime? Utc(DateTime? value) => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null;
        return Results.Ok(new StripeCompanySummary(account?.StripeCustomerId, account?.StripeSubscriptionId,
            account?.SubscriptionStatus, Utc(account?.CurrentPeriodStartUtc), Utc(account?.CurrentPeriodEndUtc), machines.Count,
            TestActionsEnabled(settings)) { BillingAccountId = account?.Id, ActiveMachines = machines, Machines=machineStates,
                CancelAtPeriodEnd=account?.CancelAtPeriodEnd ?? false,LatestInvoiceId=account?.LatestInvoiceId,
                LatestInvoiceStatus=account?.LatestInvoiceStatus,AmountRemainingCents=account?.AmountRemainingCents,
                MachineRequestProvisioningCompleted=machineRequestProvisioningCompleted });
    }

    public static async Task<IResult> ReadAdditionsAsync(Guid companyId, DiagLinkDbContext db, CancellationToken ct)
    {
        if (!await db.Companies.AnyAsync(c => c.Id == companyId, ct)) return Results.NotFound();
        var rows = await (from op in db.StripeMachineAdditions.AsNoTracking()
            join machine in db.Machines.AsNoTracking() on op.MachineId equals machine.Id
            where op.CompanyId == companyId
            orderby op.CreatedAtUtc descending
            select new { Op = op, machine.Name }).ToListAsync(ct);
        static DateTime Utc(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc);
        return Results.Ok(rows.Select(row =>
        {
            var op = row.Op;
            return new StripeAdditionSummary(op.Id, op.MachineId, row.Name, op.Stage.ToString(), op.StripeInvoiceId,
                op.TargetQuantity, op.AiAmountCents / 100m, op.ServiceAmountCents / 100m,
                Utc(op.ActivatedAtUtc), Utc(op.CycleStartUtc), Utc(op.CycleEndUtc),
                op.PaymentConfirmedAtUtc is { } paid ? Utc(paid) : null, op.MachineBillingPeriodId,
                op.ExternalEventId, op.CompletedAtUtc is { } completed ? Utc(completed) : null,
                op.CompletedAtUtc == null && DateTime.UtcNow >= Utc(op.CycleEndUtc));
        }).ToArray());
    }

    public static async Task<IResult> AddMachineAsync(Guid companyId, Guid machineId, DiagLinkDbContext db,
        StripeMachineAdditionService additions, StripeBillingOptions settings, CancellationToken ct)
    {
        if (!TestActionsEnabled(settings)) return Results.Conflict(new { error = "Ajout disponible uniquement en mode Stripe test." });
        if (!await db.Machines.AnyAsync(m => m.Id == machineId && m.CompanyId == companyId, ct)) return Results.NotFound();
        var now = DateTime.UtcNow;
        var existing = await db.StripeMachineAdditions.AsNoTracking().Where(a => a.MachineId == machineId && (a.CompletedAtUtc == null || a.CycleEndUtc > now))
            .OrderByDescending(a => a.CreatedAtUtc).FirstOrDefaultAsync(ct);
        if (existing != null && existing.CompanyId != companyId) return Results.Conflict(new { error = "Entreprise incohérente : réconciliation requise." });
        // No caller-supplied dates, prices or quantity. First command timestamps the addition;
        // every replay uses its durable original timestamp, including after payment.
        var activation = existing == null ? DateTime.UtcNow : DateTime.SpecifyKind(existing.ActivatedAtUtc, DateTimeKind.Utc);
        try { return Results.Ok(await additions.AddActiveMachineAsync(machineId, activation, ct)); }
        catch (InvalidOperationException)
        { return Results.Conflict(new { error = "Ajout non confirmé. Vérifiez le cycle, les machines actives et les opérations en cours ; une réconciliation peut être nécessaire." }); }
        catch (Stripe.StripeException)
        { return Results.Json(new { error = "Stripe n’a pas confirmé l’ajout. Réessayez la même machine." }, statusCode: 502); }
    }

    public static async Task<IResult> WriteAsync(Guid companyId, bool subscription, DiagLinkDbContext db,
        StripeBillingService billing, StripeBillingOptions settings, CancellationToken ct)
    {
        if (!TestActionsEnabled(settings)) return Results.Conflict(new { error = "Actions disponibles uniquement avec Stripe activé en mode test." });
        if (!await db.Companies.AnyAsync(c => c.Id == companyId, ct)) return Results.NotFound();
        try
        {
            if (subscription) await billing.GetOrCreateSubscriptionAsync(companyId, ct);
            else await billing.GetOrCreateCustomerAsync(companyId, ct);
            return await ReadAsync(companyId, db, settings, ct);
        }
        catch (InvalidOperationException)
        { return Results.Conflict(new { error = "Opération impossible : vérifiez l’entreprise, les machines actives et la configuration Stripe. Une réconciliation peut être nécessaire." }); }
        catch (Stripe.StripeException)
        { return Results.Json(new { error = "Stripe n’a pas confirmé l’opération. Réessayez avec la même entreprise." }, statusCode: 502); }
    }
}
