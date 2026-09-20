using System.Data;
using Microsoft.EntityFrameworkCore;
using WebApp.Api.Data;
using WebApp.Api.Models.Entities;
namespace WebApp.Api.Services;

public record WalletTopUpResult(Guid Id, string Stage, string Status, decimal AmountEur, string? PaymentUrl,
    string? StripeSessionId, string? StripePaymentIntentId, Guid? LedgerEntryId,
    string? ExternalEventId, DateTime CreatedAtUtc, DateTime? PaymentConfirmedAtUtc, DateTime? CompletedAtUtc);

public sealed class CompanyWalletTopUpService(DbContextOptions<DiagLinkDbContext> options,
    StripeBillingService billing, IStripeWalletTopUpGateway gateway, StripeBillingOptions settings)
{
    public static int AmountCents(decimal amount, string currency)
    {
        // Stripe supports up to eight amount digits. Reject fractional cents rather than rounding money silently.
        if (currency != "EUR" || amount < 10m || amount > 999999.99m || decimal.Round(amount, 2) != amount)
            throw new ArgumentException("EUR uniquement, minimum 10 €, maximum technique 999 999,99 €, deux décimales au plus.");
        return checked((int)(amount * 100m));
    }
    public static bool Configured(StripeBillingOptions settings) => StripeAdminEndpoints.TestActionsEnabled(settings)
        && Uri.TryCreate(settings.TopUpReturnUrl, UriKind.Absolute, out var uri)
        && settings.TopUpReturnUrl.Length <= 2048 && string.IsNullOrEmpty(uri.UserInfo)
        && (uri.Scheme == "https" || uri.Scheme == "http" && uri.IsLoopback)
        && settings.TopUpWebhookSecret.StartsWith("whsec_", StringComparison.Ordinal);
    private void RequireTest()
    { if (!StripeAdminEndpoints.TestActionsEnabled(settings)) throw new InvalidOperationException("Stripe test required."); }

    public async Task<WalletTopUpResult> StartAsync(Guid companyId, Guid requestId, decimal amount,
        string currency = "EUR", CancellationToken ct = default)
    {
        RequireTest(); var cents = AmountCents(amount, currency);
        if (requestId == Guid.Empty) throw new ArgumentException("Identifiant d’opération obligatoire.");
        StripeWalletTopUp? op;
        await using (var db = new DiagLinkDbContext(options))
            op = await db.StripeWalletTopUps.AsNoTracking().SingleOrDefaultAsync(o => o.Id == requestId, ct);
        if (op == null)
        {
            if (!Configured(settings)) throw new InvalidOperationException("Checkout test not configured.");
            var customer = await billing.GetOrCreateCustomerAsync(companyId, ct);
            op = await Transaction(companyId, async db =>
            {
                var existing = await db.StripeWalletTopUps.SingleOrDefaultAsync(o => o.Id == requestId, ct);
                if (existing != null) return existing;
                if (!await db.Companies.AnyAsync(c => c.Id == companyId && c.Status == "active", ct))
                    throw new InvalidOperationException("Active company required.");
                var account = await db.BillingAccounts.SingleAsync(a => a.CompanyId == companyId, ct);
                if (account.StripeCustomerId != customer) throw new InvalidOperationException("Customer mismatch.");
                var wallet = await db.CompanyWallets.AsNoTracking().SingleOrDefaultAsync(w => w.CompanyId == companyId, ct);
                if (wallet != null && wallet.Currency != "EUR") throw new InvalidOperationException("Wallet currency mismatch.");
                var created = new StripeWalletTopUp { Id = requestId, CompanyId = companyId, AmountCents = cents,
                    StripeCustomerId = customer, ReturnUrl = settings.TopUpReturnUrl, CreatedAtUtc = DateTime.UtcNow };
                db.StripeWalletTopUps.Add(created); return created;
            }, ct);
        }
        if (op.CompanyId != companyId || op.AmountCents != cents || op.Currency != currency)
            throw new InvalidOperationException("Replay inputs differ from the reserved operation.");
        if (op.Stage == StripeWalletTopUpStage.Completed) return Result(op, "AlreadyCompleted");
        if (op.Stage >= StripeWalletTopUpStage.PaymentConfirmed) return await CreditAndComplete(op, ct);
        if (op.Stage == StripeWalletTopUpStage.Reserved)
        {
            if (DateTime.UtcNow - op.CreatedAtUtc >= TimeSpan.FromHours(23)) return Result(op, "ReconciliationRequired");
            var session = await gateway.CreateAsync(op, ct);
            if (string.IsNullOrEmpty(session.SessionId)) throw new InvalidOperationException("Missing checkout identity.");
            op = await Transaction(companyId, async db =>
            {
                var current = await db.StripeWalletTopUps.SingleAsync(o => o.Id == op.Id, ct);
                if (current.StripeSessionId != null && current.StripeSessionId != session.SessionId)
                    throw new InvalidOperationException("Conflicting checkout session.");
                if (current.Stage == StripeWalletTopUpStage.Reserved)
                {
                    current.StripeSessionId = session.SessionId;
                    current.PaymentUrl = StripeWalletTopUpGateway.SafeUrl(session.Url);
                    current.Stage = StripeWalletTopUpStage.PaymentCreated;
                }
                return current;
            }, ct);
        }
        op = await Transaction(companyId, async db =>
        {
            var current = await db.StripeWalletTopUps.SingleAsync(o => o.Id == op.Id, ct);
            if (current.Stage == StripeWalletTopUpStage.PaymentCreated) current.Stage = StripeWalletTopUpStage.AwaitingPayment;
            return current;
        }, ct);
        return Result(op);
    }

    // Invoked only after mandatory webhook signature verification. No browser paid flag is accepted.
    public async Task<WalletTopUpResult?> ConfirmAsync(Guid operationId, string sessionId, string eventId, CancellationToken ct = default)
    {
        RequireTest();
        if (!eventId.StartsWith("evt_", StringComparison.Ordinal) || eventId.Length > 200 || string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("Invalid payment event identity.");
        StripeWalletTopUp? op;
        await using (var db = new DiagLinkDbContext(options))
            op = await db.StripeWalletTopUps.AsNoTracking().SingleOrDefaultAsync(o => o.Id == operationId, ct);
        if (op == null) return null;
        if (op.StripeSessionId != null && op.StripeSessionId != sessionId) throw new InvalidOperationException("Session mismatch.");
        if (op.Stage == StripeWalletTopUpStage.Completed) return Result(op, "AlreadyCompleted");
        var proof = await gateway.ReadAsync(op, sessionId, ct);
        if (proof.Status != "PaymentConfirmed") return Result(op, proof.Status);
        if (proof.SessionId != sessionId || string.IsNullOrWhiteSpace(proof.PaymentIntentId) || proof.PaymentIntentId.Length > 200
            || proof.PaidAtUtc is not { Kind: DateTimeKind.Utc } paid || paid > DateTime.UtcNow
            || paid < new DateTime(op.CreatedAtUtc.Ticks - op.CreatedAtUtc.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc))
            throw new InvalidOperationException("Invalid payment proof.");
        op = await Transaction(op.CompanyId, async db =>
        {
            var current = await db.StripeWalletTopUps.SingleAsync(o => o.Id == op.Id, ct);
            var account = await db.BillingAccounts.SingleOrDefaultAsync(a => a.CompanyId == op.CompanyId, ct);
            if (account?.StripeCustomerId != op.StripeCustomerId
                || current.StripeSessionId != null && current.StripeSessionId != sessionId
                || current.StripePaymentIntentId != null && (current.StripePaymentIntentId != proof.PaymentIntentId || current.PaymentConfirmedAtUtc != paid))
                throw new InvalidOperationException("Payment ownership/checkpoint mismatch.");
            if (await db.StripeWalletTopUps.AnyAsync(o => o.Id != op.Id && (o.ExternalEventId == eventId || o.StripePaymentIntentId == proof.PaymentIntentId), ct)
                || await db.CreditLedger.AnyAsync(l => l.ExternalEventId == eventId && l.Id != op.Id, ct))
                throw new InvalidOperationException("Payment/event already bound to another operation.");
            if (current.Stage < StripeWalletTopUpStage.PaymentConfirmed)
            {
                // Can recover a lost Checkout creation SQL checkpoint using signed metadata + remote proof.
                current.StripeSessionId = sessionId; current.StripePaymentIntentId = proof.PaymentIntentId;
                current.PaymentConfirmedAtUtc = paid; current.ExternalEventId = eventId;
                current.PaymentUrl = null; current.Stage = StripeWalletTopUpStage.PaymentConfirmed;
            }
            return current;
        }, ct);
        return await CreditAndComplete(op, ct);
    }

    private async Task<WalletTopUpResult> CreditAndComplete(StripeWalletTopUp op, CancellationToken ct)
    {
        op = await Transaction(op.CompanyId, async db =>
        {
            var current = await db.StripeWalletTopUps.SingleAsync(o => o.Id == op.Id, ct);
            if (current.Stage >= StripeWalletTopUpStage.WalletCredited) return current;
            if (current.Stage != StripeWalletTopUpStage.PaymentConfirmed || current.ExternalEventId == null
                || current.StripePaymentIntentId == null || current.PaymentConfirmedAtUtc == null)
                throw new InvalidOperationException("Confirmed payment required.");
            // Same wallet lock used by CompanyWalletDebitService, including the absent-wallet key range.
            var wallets = db.Database.IsSqlServer()
                ? db.CompanyWallets.FromSqlInterpolated($"SELECT * FROM [dbo].[CompanyWallets] WITH (UPDLOCK, HOLDLOCK) WHERE [CompanyId] = {op.CompanyId}")
                : db.CompanyWallets.AsQueryable();
            var wallet = await wallets.SingleOrDefaultAsync(w => w.CompanyId == op.CompanyId, ct);
            var now = DateTime.UtcNow;
            if (wallet == null)
            {
                wallet = new CompanyWallet { CompanyId = op.CompanyId, Balance = 0m, Currency = "EUR", CreatedAtUtc = now, UpdatedAtUtc = now };
                db.CompanyWallets.Add(wallet);
            }
            if (wallet.Currency != "EUR" || wallet.Balance < 0 || decimal.Round(wallet.Balance, 6) != wallet.Balance
                || wallet.Balance > 999999999999.999999m - current.AmountCents / 100m)
                throw new InvalidOperationException("Invalid wallet balance or currency.");
            if (await db.CreditLedger.AnyAsync(l => l.Id == current.Id || l.ExternalEventId == current.ExternalEventId, ct))
                throw new InvalidOperationException("Unexpected existing ledger; reconciliation required.");
            wallet.Balance += current.AmountCents / 100m; wallet.UpdatedAtUtc = now;
            db.CreditLedger.Add(new CreditLedgerEntry { Id = current.Id, CompanyId = current.CompanyId,
                EntryType = "TopUp", BucketType = "CompanyWallet", CommercialCreditAmount = current.AmountCents / 100m,
                BalanceAfter = wallet.Balance, Currency = "EUR", ExternalEventId = current.ExternalEventId, CreatedAtUtc = now });
            current.LedgerEntryId = current.Id; current.Stage = StripeWalletTopUpStage.WalletCredited;
            return current; // Wallet + immutable ledger + checkpoint are committed together.
        }, ct);
        op = await Transaction(op.CompanyId, async db =>
        {
            var current = await db.StripeWalletTopUps.SingleAsync(o => o.Id == op.Id, ct);
            if (current.Stage == StripeWalletTopUpStage.WalletCredited)
            { current.Stage = StripeWalletTopUpStage.Completed; current.CompletedAtUtc = DateTime.UtcNow; }
            return current;
        }, ct);
        return Result(op);
    }

    private async Task<T> Transaction<T>(Guid companyId, Func<DiagLinkDbContext, Task<T>> action, CancellationToken ct)
    {
        await using var strategy = new DiagLinkDbContext(options);
        return await strategy.Database.CreateExecutionStrategy().ExecuteAsync(async token =>
        {
            await using var db = new DiagLinkDbContext(options);
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            var companies = db.Database.IsSqlServer()
                ? db.Companies.FromSqlInterpolated($"SELECT * FROM [dbo].[Companies] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {companyId}")
                : db.Companies.AsQueryable();
            if (await companies.SingleOrDefaultAsync(c => c.Id == companyId, token) == null) throw new InvalidOperationException("Company missing.");
            var result = await action(db); await db.SaveChangesAsync(token); await tx.CommitAsync(token); return result;
        }, ct);
    }
    public static WalletTopUpResult Result(StripeWalletTopUp op, string? status = null) => new(op.Id, op.Stage.ToString(),
        status ?? op.Stage.ToString(), op.AmountCents / 100m, op.Stage < StripeWalletTopUpStage.PaymentConfirmed ? op.PaymentUrl : null,
        op.StripeSessionId, op.StripePaymentIntentId, op.LedgerEntryId, op.ExternalEventId,
        DateTime.SpecifyKind(op.CreatedAtUtc, DateTimeKind.Utc),
        op.PaymentConfirmedAtUtc is { } paid ? DateTime.SpecifyKind(paid, DateTimeKind.Utc) : null,
        op.CompletedAtUtc is { } completed ? DateTime.SpecifyKind(completed, DateTimeKind.Utc) : null);
}
