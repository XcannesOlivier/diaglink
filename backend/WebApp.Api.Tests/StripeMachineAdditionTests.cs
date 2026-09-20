using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;
using Fixture = WebApp.Api.Tests.AiCreditConsumptionServiceTests.Fixture;

namespace WebApp.Api.Tests;

[TestClass]
public partial class StripeMachineAdditionTests
{
    [TestMethod]
    public async Task ReactivationAfterExpiredHistoryUsesNewAdditionAndPreservesOldOperation()
    {
        await using var f=new Fixture();var machine=await Prepare(f);var remote=new Remote();
        var first=await Service(f,remote).AddActiveMachineAsync(machine,Activation);
        Assert.AreEqual("Completed",first.Status);
        await using(var db=f.Db())
        {
            var op=await db.StripeMachineAdditions.SingleAsync();op.CycleStartUtc=op.CycleStartUtc.AddMonths(-1);
            op.CycleEndUtc=op.CycleEndUtc.AddMonths(-1);op.ActivatedAtUtc=op.ActivatedAtUtc.AddMonths(-1);
            var p=await db.MachineBillingPeriods.SingleAsync(p=>p.MachineId==machine);
            p.PeriodStartUtc=p.PeriodStartUtc.AddMonths(-1);p.PeriodEndUtc=p.PeriodEndUtc.AddMonths(-1);p.Status="Closed";
            await db.SaveChangesAsync();
        }
        remote.Quantity=1;remote.PaymentStatus="open";
        var second=await Service(f,remote).AddActiveMachineAsync(machine,Activation);
        Assert.AreEqual("AwaitingPayment",second.Status);Assert.AreNotEqual(first.OperationId,second.OperationId);
        remote.PaymentStatus="paid";
        Assert.AreEqual("Completed",(await Service(f,remote).AddActiveMachineAsync(machine,Activation)).Status);
        await using var after=f.Db();Assert.AreEqual(2,await after.StripeMachineAdditions.CountAsync());
        Assert.AreEqual(2,await after.MachineBillingPeriods.CountAsync(p=>p.MachineId==machine));
    }
    private static StripeBillingOptions Settings => new() { Enabled = true, SecretKey = "sk_test_local", PriceId = "price_local" };
    private static readonly DateTime Start = DateTime.UtcNow.Date.AddDays(-15);
    private static readonly DateTime End = Start.AddDays(30);
    private static readonly DateTime Activation = Start.AddDays(15);

    private sealed class Remote : IStripeBillingGateway, IStripeMachineAdditionGateway
    {
        public long Quantity = 1;
        public bool AllowProvisioning;
        public int CustomersCreated, SubscriptionsCreated;
        public int InvoiceCount, FinalizeCount, QuantityWrites, Calls;
        public string? FailAfter;
        public string PaymentStatus = "paid";
        public int FailPaymentReads;
        public DateTime PaidAtUtc = DateTime.UtcNow;
        public HashSet<string> Effects { get; } = [];
        public List<int> Amounts { get; } = [];
        public Func<Task>? BeforeQuantity;
        private void Effect(string key, Action apply)
        {
            lock (Effects)
            {
                Calls++;
                if (Effects.Add(key)) apply();
                if (FailAfter == key.Split(':')[^1]) { FailAfter = null; throw new TimeoutException("Simulated lost response"); }
            }
        }
        public Task ValidatePriceAsync(CancellationToken ct) => Task.CompletedTask;
        public Task PrepareInitialPaymentAsync(string id, string customer, Guid company, CancellationToken ct) => Task.CompletedTask;
        public Task<string> CreateCustomerAsync(Guid c, Guid a, CancellationToken ct)
        {
            if (!AllowProvisioning) throw new AssertFailedException("Must not create customer");
            CustomersCreated++; return Task.FromResult("cus_local");
        }
        public Task<string> GetCustomerAsync(string c, Guid id, CancellationToken ct) => Task.FromResult(c);
        public Task<StripeSubscriptionSnapshot> CreateSubscriptionAsync(string c, Guid id, Guid a, int q, CancellationToken ct)
        {
            if (!AllowProvisioning) throw new AssertFailedException("Must not create subscription");
            SubscriptionsCreated++; Quantity = q;
            return GetSubscriptionAsync("sub_local", c, id, ct);
        }
        public Task<StripeSubscriptionSnapshot> GetSubscriptionAsync(string id, string customer, Guid company, CancellationToken ct)
            => Task.FromResult(new StripeSubscriptionSnapshot(id, customer, "active", Start, End, Quantity) { ItemId = "si_local" });
        public async Task SetQuantityAsync(StripeMachineAddition op, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (BeforeQuantity != null) await BeforeQuantity();
            Effect(op.Id + ":quantity", () => { Quantity = op.TargetQuantity; QuantityWrites++; });
        }
        public Task<string> CreateInvoiceAsync(StripeMachineAddition op, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Effect(op.Id + ":invoice", () => InvoiceCount++);
            return Task.FromResult("in_" + op.Id.ToString("N"));
        }
        public Task AddInvoiceLinesAsync(StripeMachineAddition op, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Effect(op.Id + ":ai", () => Amounts.Add(op.AiAmountCents));
            Effect(op.Id + ":service", () => Amounts.Add(op.ServiceAmountCents));
            return Task.CompletedTask;
        }
        public Task FinalizeInvoiceAsync(StripeMachineAddition op, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Effect(op.Id + ":finalize", () => FinalizeCount++);
            return Task.CompletedTask;
        }
        public Task<StripeAdditionInvoiceState> GetInvoicePaymentAsync(StripeMachineAddition op, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (FailPaymentReads-- > 0) throw new TimeoutException("Simulated payment read failure");
            lock (Effects)
            {
                Calls++;
                if (!Effects.Contains(op.Id + ":finalize")) return Task.FromResult(new StripeAdditionInvoiceState("draft"));
                return Task.FromResult(new StripeAdditionInvoiceState(PaymentStatus, PaymentStatus == "paid"
                    ? new StripePaymentConfirmation("[\"inpay_local\"]", PaidAtUtc) : null));
            }
        }
    }

    private static StripeMachineAdditionService Service(Fixture f, Remote remote) => new(f.Options,
        new StripeBillingService(f.Options, remote, Settings), new MachineBillingPeriodService(f.Options), remote, Settings);

    private static async Task<Guid> Prepare(Fixture f)
    {
        await f.Seed();
        var machineId = Guid.NewGuid();
        await using var db = f.Db();
        (await db.Companies.SingleAsync()).Status = "active";
        (await db.Machines.SingleAsync()).Status = "active";
        db.Machines.Add(new Machine { Id = machineId, CompanyId = f.CompanyId, Name = "new", Status = "active" });
        db.BillingAccounts.Add(new BillingAccount { Id = Guid.NewGuid(), CompanyId = f.CompanyId,
            StripeCustomerId = "cus_local", StripeSubscriptionId = "sub_local", SubscriptionStatus = "active",
            CurrentPeriodStartUtc = Start, CurrentPeriodEndUtc = End, CreatedAtUtc = Start, UpdatedAtUtc = Start });
        await db.SaveChangesAsync();
        return machineId;
    }

    [TestMethod]
    [DataRow(30, 0, 1990)]
    [DataRow(30, 15, 995)]
    [DataRow(30, 29, 66)]
    [DataRow(28, 27, 71)]
    [DataRow(31, 30, 64)]
    [DataRow(28, 7, 1493)]
    public void ProrataUsesActualCycleDurationAndRoundsServiceOnly(int days, int elapsed, int cents)
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.AreEqual(cents, StripeMachineAdditionService.CalculateServiceCents(start, start.AddDays(days), start.AddDays(elapsed)));
        Assert.AreEqual(0, StripeMachineAdditionService.CalculateServiceCents(start, start.AddDays(days), start.AddDays(days).AddTicks(-1)));
    }

    [TestMethod]
    public void InvalidProrataDatesAreRejected()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => StripeMachineAdditionService.CalculateServiceCents(Start, End, End));
        Assert.ThrowsExactly<InvalidOperationException>(() => StripeMachineAdditionService.CalculateServiceCents(Start, End, Start.AddTicks(-1)));
        Assert.ThrowsExactly<InvalidOperationException>(() => StripeMachineAdditionService.CalculateServiceCents(Start, Start, Start));
        Assert.ThrowsExactly<InvalidOperationException>(() => StripeMachineAdditionService.CalculateServiceCents(Start, End, DateTime.SpecifyKind(Activation, DateTimeKind.Local)));
    }

    [TestMethod]
    public async Task FullBudgetOnlyAfterPaymentAndCompletedReplayDoesNotCallStripe()
    {
        await using var f = new Fixture(); var machine = await Prepare(f); var remote = new Remote();
        remote.BeforeQuantity = async () =>
        {
            await using var db = f.Db();
            Assert.AreEqual(0, await db.MachineBillingPeriods.CountAsync(p => p.MachineId == machine));
        };
        var result = await Service(f, remote).AddActiveMachineAsync(machine, Activation);
        Assert.AreEqual("Completed", result.Status); Assert.AreEqual(10m, result.AiAmountEur); Assert.AreEqual(9.95m, result.ServiceAmountEur);
        var calls = remote.Calls;
        Assert.AreEqual("AlreadyCompleted", (await Service(f, remote).AddActiveMachineAsync(machine, Activation)).Status);
        Assert.AreEqual(calls, remote.Calls);
        await Check(f, machine, remote);
    }

    private static async Task Check(Fixture f, Guid machine, Remote remote)
    {
        Assert.AreEqual(2L, remote.Quantity); Assert.AreEqual(1, remote.QuantityWrites);
        Assert.AreEqual(1, remote.InvoiceCount); Assert.AreEqual(1, remote.FinalizeCount);
        CollectionAssert.AreEqual(new[] { 1000, 995 }, remote.Amounts);
        await using var db = f.Db();
        var period = await db.MachineBillingPeriods.SingleAsync(p => p.MachineId == machine);
        Assert.AreEqual(Activation, period.PeriodStartUtc); Assert.AreEqual(End, period.PeriodEndUtc);
        Assert.AreEqual(10m, period.IncludedAiBudgetRealCost); Assert.AreEqual(0m, period.IncludedAiUsedRealCost);
        Assert.AreEqual("Active", period.Status);
        var account = await db.BillingAccounts.SingleAsync(); Assert.AreEqual(Start, account.CurrentPeriodStartUtc); Assert.AreEqual(End, account.CurrentPeriodEndUtc);
        Assert.AreEqual(0, await db.CreditLedger.CountAsync()); Assert.AreEqual(0, await db.CompanyWallets.CountAsync());
        Assert.AreEqual(1, await db.StripeMachineAdditions.CountAsync());
        Assert.IsNotNull((await db.StripeMachineAdditions.SingleAsync()).CompletedAtUtc);
        var operation = await db.StripeMachineAdditions.SingleAsync();
        Assert.AreEqual(StripeMachineAdditionStage.Completed, operation.Stage);
        Assert.AreEqual("[\"inpay_local\"]", operation.PaymentReference);
        Assert.AreEqual(remote.PaidAtUtc, operation.PaymentConfirmedAtUtc);
    }

    [TestMethod]
    [DataRow("quantity")]
    [DataRow("invoice")]
    [DataRow("ai")]
    [DataRow("service")]
    [DataRow("finalize")]
    public async Task LostStripeResponseResumesWithoutDuplicateEffects(string failure)
    {
        await using var f = new Fixture(); var machine = await Prepare(f); var remote = new Remote { FailAfter = failure };
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => Service(f, remote).AddActiveMachineAsync(machine, Activation));
        await Service(f, remote).AddActiveMachineAsync(machine, Activation);
        await Check(f, machine, remote);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    [DataRow(6)]
    public async Task FailedSqlCheckpointResumesFromDurableInputs(int stage)
    {
        await using var f = new Fixture(); var machine = await Prepare(f); var remote = new Remote();
        await using (var db = f.Db())
        {
            // DDL cannot parameterize the stage; it is restricted to these five local test literals.
            var stageLiteral = stage switch { 1 => "1", 2 => "2", 3 => "3", 4 => "4", 5 => "5", 6 => "6", _ => throw new ArgumentOutOfRangeException(nameof(stage)) };
            var sql = "CREATE TRIGGER fail_checkpoint BEFORE UPDATE ON StripeMachineAdditions WHEN NEW.Stage = " + stageLiteral + " BEGIN SELECT RAISE(ABORT,'test'); END";
            await db.Database.ExecuteSqlRawAsync(sql);
        }
        await Assert.ThrowsExactlyAsync<DbUpdateException>(() => Service(f, remote).AddActiveMachineAsync(machine, Activation));
        await using (var db = f.Db()) await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_checkpoint");
        await Service(f, remote).AddActiveMachineAsync(machine, Activation);
        await Check(f, machine, remote);
    }

    [TestMethod]
    public async Task ExpiredUncertainOperationIsBlockedWithoutNewStripeCall()
    {
        await using var f = new Fixture(); var machine = await Prepare(f); var remote = new Remote { FailAfter = "invoice" };
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => Service(f, remote).AddActiveMachineAsync(machine, Activation));
        await using (var db = f.Db())
        {
            (await db.StripeMachineAdditions.SingleAsync()).CreatedAtUtc = DateTime.UtcNow.AddDays(-2);
            await db.SaveChangesAsync();
        }
        var calls = remote.Calls;
        Assert.AreEqual("ReconciliationRequired", (await Service(f, remote).AddActiveMachineAsync(machine, Activation)).Status);
        Assert.AreEqual(calls, remote.Calls); Assert.AreEqual(1, remote.InvoiceCount);
    }

    [TestMethod]
    [DataRow("inactive")]
    [DataRow("existing-period")]
    [DataRow("stale-cycle")]
    [DataRow("quantity-covered")]
    public async Task InvalidAdditionDoesNotMutateStripe(string reason)
    {
        await using var f = new Fixture(); var machine = await Prepare(f); var remote = new Remote();
        await using (var db = f.Db())
        {
            if (reason == "inactive") (await db.Machines.SingleAsync(m => m.Id == machine)).Status = "inactive";
            if (reason == "stale-cycle") (await db.BillingAccounts.SingleAsync()).CurrentPeriodEndUtc = End.AddDays(1);
            await db.SaveChangesAsync();
        }
        if (reason == "existing-period") machine = f.MachineId;
        if (reason == "quantity-covered") remote.Quantity = 2;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Service(f, remote).AddActiveMachineAsync(machine, Activation));
        Assert.AreEqual(0, remote.Calls);
        await using var check = f.Db(); Assert.AreEqual(0, await check.StripeMachineAdditions.CountAsync());
    }

    [TestMethod]
    public async Task DifferentMachinesAreSerializedThenEachAddsExactlyOne()
    {
        await using var f = new Fixture(); var machine = await Prepare(f); var remote = new Remote { FailAfter = "quantity" };
        var other = Guid.NewGuid();
        await using (var db = f.Db()) { db.Machines.Add(new Machine { Id = other, CompanyId = f.CompanyId, Name = "second", Status = "active" }); await db.SaveChangesAsync(); }
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => Service(f, remote).AddActiveMachineAsync(machine, Activation));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Service(f, remote).AddActiveMachineAsync(other, Activation));
        await Service(f, remote).AddActiveMachineAsync(machine, Activation);
        await Service(f, remote).AddActiveMachineAsync(other, Activation);
        Assert.AreEqual(3L, remote.Quantity); Assert.AreEqual(2, remote.InvoiceCount);
        await using var check = f.Db(); Assert.AreEqual(2, await check.StripeMachineAdditions.CountAsync());
        Assert.AreEqual(3, await check.MachineBillingPeriods.CountAsync());
    }

    [TestMethod]
    public async Task CancellationAndChangedReplayTimestampDoNotAddEffects()
    {
        await using var f = new Fixture(); var machine = await Prepare(f); var remote = new Remote();
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => Service(f, remote).AddActiveMachineAsync(machine, Activation, cts.Token));
        Assert.AreEqual(0, remote.Calls);
        await Service(f, remote).AddActiveMachineAsync(machine, Activation);
        var calls = remote.Calls;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Service(f, remote).AddActiveMachineAsync(machine, Activation.AddSeconds(1)));
        Assert.AreEqual(calls, remote.Calls);
    }

    [TestMethod]
    public async Task ConcurrentCallsForSameMachineHaveOnePeriodAndInvoice()
    {
        await using var f = new Fixture(); var machine = await Prepare(f); var remote = new Remote();
        async Task Run()
        {
            for (var attempt = 0; ; attempt++)
            {
                try { await Service(f, remote).AddActiveMachineAsync(machine, Activation); return; }
                catch (SqliteException ex) when (attempt < 5 && ex.SqliteErrorCode is 5 or 6) { await Task.Delay(20); }
                catch (DbUpdateException ex) when (attempt < 5 && ex.InnerException is SqliteException { SqliteErrorCode: 5 or 6 }) { await Task.Delay(20); }
            }
        }
        await Task.WhenAll(Task.Run(Run), Task.Run(Run));
        await Check(f, machine, remote);
    }

    [TestMethod]
    [DataRow("open")]
    [DataRow("processing")]
    [DataRow("failed")]
    public async Task UnpaidInvoiceNeverGrantsBudgetAndPaymentAfter23HoursStillResumes(string status)
    {
        await using var f = new Fixture(); var machine = await Prepare(f); var remote = new Remote { PaymentStatus = status };
        var awaiting = await Service(f, remote).AddActiveMachineAsync(machine, Activation);
        Assert.AreEqual("AwaitingPayment", awaiting.Status); Assert.IsNull(awaiting.BillingPeriodId);
        await using (var db = f.Db())
        {
            Assert.AreEqual(0, await db.MachineBillingPeriods.CountAsync(p => p.MachineId == machine));
            var op = await db.StripeMachineAdditions.SingleAsync();
            Assert.AreEqual(StripeMachineAdditionStage.AwaitingPayment, op.Stage);
            Assert.IsNull(op.PaymentReference); Assert.IsNull(op.PaymentConfirmedAtUtc); Assert.IsNull(op.CompletedAtUtc);
            op.CreatedAtUtc = DateTime.UtcNow.AddDays(-2);
            await db.SaveChangesAsync();
        }
        Assert.AreEqual("AwaitingPayment", (await Service(f, remote).AddActiveMachineAsync(machine, Activation)).Status);
        remote.PaymentStatus = "paid";
        var paid = await Service(f, remote).AddActiveMachineAsync(machine, Activation);
        Assert.AreEqual("Completed", paid.Status); Assert.AreEqual(awaiting.InvoiceId, paid.InvoiceId);
        await Check(f, machine, remote);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task LatePaymentIsPersistedButNeverCreatesExpiredPeriod(bool paymentItselfLate)
    {
        await using var f = new Fixture(); var machine = await Prepare(f); var remote = new Remote();
        var end = DateTime.UtcNow.Date.AddDays(-1); var activation = end.AddDays(-10);
        var op = new StripeMachineAddition
        {
            Id = Guid.NewGuid(), MachineId = machine, CompanyId = f.CompanyId,
            StripeCustomerId = "cus_local", StripeSubscriptionId = "sub_local", StripeSubscriptionItemId = "si_local", StripePriceId = "price_local",
            ActivatedAtUtc = activation, CycleStartUtc = end.AddDays(-30), CycleEndUtc = end,
            AiAmountCents = 1000, ServiceAmountCents = 663, OriginalQuantity = 1, TargetQuantity = 2,
            StripeInvoiceId = "in_local", Stage = StripeMachineAdditionStage.AwaitingPayment, CreatedAtUtc = activation
        };
        await using (var db = f.Db())
        {
            op.BillingAccountId = (await db.BillingAccounts.SingleAsync()).Id;
            db.StripeMachineAdditions.Add(op); await db.SaveChangesAsync();
        }
        remote.Effects.Add(op.Id + ":finalize");
        remote.PaidAtUtc = paymentItselfLate ? end.AddHours(1) : end.AddHours(-1);
        Assert.AreEqual("ReconciliationRequired", (await Service(f, remote).AddActiveMachineAsync(machine, activation)).Status);
        Assert.AreEqual("ReconciliationRequired", (await Service(f, remote).AddActiveMachineAsync(machine, activation)).Status);
        await using var check = f.Db();
        Assert.AreEqual(0, await check.MachineBillingPeriods.CountAsync(p => p.MachineId == machine));
        var saved = await check.StripeMachineAdditions.SingleAsync();
        Assert.AreEqual(StripeMachineAdditionStage.PaymentConfirmed, saved.Stage);
        Assert.AreEqual(remote.PaidAtUtc, saved.PaymentConfirmedAtUtc); Assert.IsNotNull(saved.PaymentReference);
        Assert.IsNull(saved.CompletedAtUtc); Assert.AreEqual(0, remote.InvoiceCount); Assert.AreEqual(0, remote.QuantityWrites);
    }

    [TestMethod]
    public async Task LostFinalizationCheckpointCanResumeAfter23HoursWithoutRecreatingInvoice()
    {
        await using var f = new Fixture(); var machine = await Prepare(f); var remote = new Remote { FailAfter = "finalize", PaymentStatus = "open" };
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => Service(f, remote).AddActiveMachineAsync(machine, Activation));
        await using (var db = f.Db())
        {
            var op = await db.StripeMachineAdditions.SingleAsync();
            Assert.AreEqual(StripeMachineAdditionStage.StripeQuantityUpdated, op.Stage);
            op.CreatedAtUtc = DateTime.UtcNow.AddDays(-2); await db.SaveChangesAsync();
        }
        Assert.AreEqual("AwaitingPayment", (await Service(f, remote).AddActiveMachineAsync(machine, Activation)).Status);
        remote.PaymentStatus = "paid";
        await Service(f, remote).AddActiveMachineAsync(machine, Activation);
        await Check(f, machine, remote);
    }

    [TestMethod]
    public async Task DatabaseRejectsPeriodCheckpointWithoutPaymentEvidence()
    {
        await using var f = new Fixture(); var machine = await Prepare(f); var remote = new Remote { PaymentStatus = "open" };
        await Service(f, remote).AddActiveMachineAsync(machine, Activation);
        await using var db = f.Db();
        var op = await db.StripeMachineAdditions.SingleAsync();
        op.Stage = StripeMachineAdditionStage.MachineBillingPeriodCreated; op.MachineBillingPeriodId = f.PeriodId;
        await Assert.ThrowsExactlyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
