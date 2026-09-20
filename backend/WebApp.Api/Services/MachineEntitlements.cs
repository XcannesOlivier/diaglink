using WebApp.Api.Data;
using WebApp.Api.Models.Entities;
namespace WebApp.Api.Services;
public static class MachineEntitlements
{
    // A Stripe-linked company derives access from paid periods, not the latest invoice status.
    // The legacy active-machine rule remains unchanged for companies without a subscription.
    public static IQueryable<Machine> Eligible(DiagLinkDbContext db, DateTime now) => db.Machines.Where(m =>
        db.BillingAccounts.Any(a => a.CompanyId == m.CompanyId && a.StripeSubscriptionId != null)
            ? db.MachineBillingPeriods.Any(p => p.MachineId == m.Id && p.PeriodStartUtc <= now && now < p.PeriodEndUtc
                && (p.Status == "Active" || p.Status == "Closed"))
            : m.Status == "active");
}
