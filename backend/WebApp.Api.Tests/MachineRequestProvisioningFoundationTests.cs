using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Data;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;
using PaymentEntity = WebApp.Api.Models.Entities.MachineRequestPayment;

namespace WebApp.Api.Tests;

[TestClass]
public class MachineRequestProvisioningFoundationTests
{
    [TestMethod]
    public void MachineRequestKindValuesRemainBackwardCompatible()
    {
        Assert.AreEqual(0, (int)MachineRequestKind.InitialMachine);
        Assert.AreEqual(1, (int)MachineRequestKind.AdditionalMachine);
        Assert.AreEqual(2, (int)MachineRequestKind.AdditionalDocuments);
    }

    [TestMethod]
    public async Task ExistingPaymentRowsKeepCompatibleProvisioningDefaults()
    {
        var options = new DbContextOptionsBuilder<DiagLinkDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new DiagLinkDbContext(options);
        var store = new MachineRequestPaymentStore(db);
        var now = DateTime.UtcNow;

        var saved = await store.AddAsync(new(Guid.NewGuid(), 400, 9990, "EUR", null,
            null, null, "pending", now), default);

        Assert.AreEqual(MachineRequestProvisioningStage.AwaitingAcceptance, saved.ProvisioningStage);
        Assert.IsNull(saved.ActivatedAtUtc);
        Assert.IsNull(saved.FirstPeriodEndUtc);
        Assert.IsNull(saved.ServiceAmountCents);
        Assert.IsNull(saved.FinalCaptureAmountCents);
        Assert.IsNull(saved.CompanyId);
        Assert.IsNull(saved.MachineId);
        Assert.IsNull(saved.TargetMachineId);
        Assert.IsNull(saved.ProvisioningCompletedAtUtc);
        Assert.AreEqual(MachineRequestKind.InitialMachine, saved.RequestKind);
        Assert.IsNull(saved.RequestedByUserId);
    }

    [TestMethod]
    public async Task RequestOriginFieldsPersistAndAreImmutableInTheEfModel()
    {
        var options = new DbContextOptionsBuilder<DiagLinkDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var requestedByUserId = Guid.NewGuid();
        await using var db = new DiagLinkDbContext(options);
        var store = new MachineRequestPaymentStore(db);
        var saved = await store.AddAsync(new(Guid.NewGuid(), 400, 12980, "EUR", null,
            null, null, "pending", DateTime.UtcNow,
            RequestKind: MachineRequestKind.AdditionalMachine,
            RequestedByUserId: requestedByUserId), default);

        Assert.AreEqual(MachineRequestKind.AdditionalMachine, saved.RequestKind);
        Assert.AreEqual(requestedByUserId, saved.RequestedByUserId);
        var entityType = db.Model.FindEntityType(typeof(PaymentEntity))!;
        Assert.AreEqual(MachineRequestKind.InitialMachine,
            entityType.FindProperty(nameof(PaymentEntity.RequestKind))!.GetDefaultValue());
        Assert.AreEqual(PropertySaveBehavior.Throw,
            entityType.FindProperty(nameof(PaymentEntity.RequestKind))!.GetAfterSaveBehavior());
        Assert.AreEqual(PropertySaveBehavior.Throw,
            entityType.FindProperty(nameof(PaymentEntity.RequestedByUserId))!.GetAfterSaveBehavior());

        var persisted = await db.MachineRequestPayments.SingleAsync(item => item.Id == saved.PaymentRequestId);
        persisted.RequestKind = MachineRequestKind.InitialMachine;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [TestMethod]
    public void SqlConstraintAllowsOnlyKnownRequestKindsIncludingAdditionalDocuments()
    {
        using var db = new DiagLinkDbContext(new DbContextOptionsBuilder<DiagLinkDbContext>()
            .UseSqlite("Data Source=:memory:").Options);
        var constraint = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(PaymentEntity))!.GetCheckConstraints()
            .Single(item => item.Name == "CK_MachineRequestPayments_RequestKind");

        Assert.AreEqual("[RequestKind] >= 0 AND [RequestKind] <= 2", constraint.Sql);
    }

    [TestMethod]
    public async Task TargetMachineIdHasNonUniqueForeignKeyAndIsImmutableAfterInsertion()
    {
        var options = new DbContextOptionsBuilder<DiagLinkDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new DiagLinkDbContext(options);
        var targetMachineId = Guid.NewGuid();
        var first = Payment();
        first.RequestKind = MachineRequestKind.AdditionalDocuments;
        first.TargetMachineId = targetMachineId;
        var second = Payment();
        second.RequestKind = MachineRequestKind.AdditionalDocuments;
        second.TargetMachineId = targetMachineId;
        db.AddRange(first, second);
        await db.SaveChangesAsync();

        var entityType = db.Model.FindEntityType(typeof(PaymentEntity))!;
        var targetProperty = entityType.FindProperty(nameof(PaymentEntity.TargetMachineId))!;
        Assert.IsTrue(targetProperty.IsNullable);
        Assert.AreEqual(PropertySaveBehavior.Throw, targetProperty.GetAfterSaveBehavior());
        var targetIndex = entityType.GetIndexes().Single(index =>
            index.Properties.Select(property => property.Name).SequenceEqual([nameof(PaymentEntity.TargetMachineId)]));
        Assert.IsFalse(targetIndex.IsUnique);
        var machineIndex = entityType.GetIndexes().Single(index =>
            index.Properties.Select(property => property.Name).SequenceEqual([nameof(PaymentEntity.MachineId)]));
        Assert.IsTrue(machineIndex.IsUnique);
        var targetForeignKey = entityType.GetForeignKeys().Single(foreignKey =>
            foreignKey.Properties.Select(property => property.Name).SequenceEqual([nameof(PaymentEntity.TargetMachineId)]));
        Assert.AreEqual(typeof(Machine), targetForeignKey.PrincipalEntityType.ClrType);
        Assert.AreEqual(DeleteBehavior.Restrict, targetForeignKey.DeleteBehavior);
        Assert.AreEqual(2, await db.MachineRequestPayments.CountAsync(item => item.TargetMachineId == targetMachineId));

        first.TargetMachineId = Guid.NewGuid();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [TestMethod]
    public async Task ProvisioningFoundationFieldsPersistAcrossDbContextRestart()
    {
        var options = new DbContextOptionsBuilder<DiagLinkDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var id = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var machineId = Guid.NewGuid();
        var activated = new DateTime(2026, 9, 22, 8, 30, 0, DateTimeKind.Utc);
        var periodEnd = new DateTime(2026, 9, 30, 22, 0, 0, DateTimeKind.Utc);
        await using (var first = new DiagLinkDbContext(options))
        {
            var store = new MachineRequestPaymentStore(first);
            await store.AddAsync(new(id, 400, 12980, "EUR", null, null, null, "pending", activated,
                ActivatedAtUtc: activated, FirstPeriodEndUtc: periodEnd, ServiceAmountCents: 577,
                FinalCaptureAmountCents: 11567, CompanyId: companyId, MachineId: machineId,
                ProvisioningStage: MachineRequestProvisioningStage.BusinessEntitiesCreated), default);
        }

        await using var restarted = new DiagLinkDbContext(options);
        var saved = await new MachineRequestPaymentStore(restarted).GetAsync(id, default);
        Assert.IsNotNull(saved);
        Assert.AreEqual(activated, saved.ActivatedAtUtc);
        Assert.AreEqual(periodEnd, saved.FirstPeriodEndUtc);
        Assert.AreEqual(577, saved.ServiceAmountCents);
        Assert.AreEqual(11567, saved.FinalCaptureAmountCents);
        Assert.AreEqual(companyId, saved.CompanyId);
        Assert.AreEqual(machineId, saved.MachineId);
        Assert.AreEqual(MachineRequestProvisioningStage.BusinessEntitiesCreated, saved.ProvisioningStage);
    }

    [TestMethod]
    public async Task SqlConstraintsRejectInvalidProvisioningAmounts()
    {
        await AssertConstraintFailure(serviceAmount: -1, finalCaptureAmount: 10000);
        await AssertConstraintFailure(serviceAmount: 1991, finalCaptureAmount: 10000);
        await AssertConstraintFailure(serviceAmount: 1000, finalCaptureAmount: 0);
        await AssertConstraintFailure(serviceAmount: 1000, finalCaptureAmount: 12981);
    }

    [TestMethod]
    public async Task SqlConstraintRejectsInvalidFirstPeriodDates()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<DiagLinkDbContext>().UseSqlite(connection).Options;
        await using var db = new DiagLinkDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var instant = new DateTime(2026, 9, 22, 8, 30, 0, DateTimeKind.Utc);
        db.MachineRequestPayments.Add(WithDates(Payment(), instant, instant));

        await Assert.ThrowsExactlyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [TestMethod]
    public void FirstPeriodStartsAtActivationAndFullServiceAppliesAtLocalMonthStart()
    {
        var activation = new DateTime(2026, 9, 30, 22, 0, 0, DateTimeKind.Utc); // 1 October 00:00 CEST
        var result = MachineRequestFirstPeriodPricing.Calculate(activation);

        Assert.AreEqual(activation, result.ActivatedAtUtc);
        Assert.AreEqual(activation, result.CycleStartUtc);
        Assert.AreEqual(new DateTime(2026, 10, 31, 23, 0, 0, DateTimeKind.Utc), result.FirstPeriodEndUtc);
        Assert.AreEqual(1990, result.ServiceAmountCents);
    }

    [TestMethod]
    [DataRow("2026-09-15T10:00:00Z", "2026-09-30T22:00:00Z")]
    [DataRow("2026-09-22T10:00:00Z", "2026-09-30T22:00:00Z")]
    [DataRow("2026-09-30T20:00:00Z", "2026-09-30T22:00:00Z")]
    [DataRow("2026-12-22T10:00:00Z", "2026-12-31T23:00:00Z")]
    public void FirstPeriodEndsAtNextParisMonthBoundary(string activationText, string expectedEndText)
    {
        var activation = DateTime.Parse(activationText, null, System.Globalization.DateTimeStyles.AdjustToUniversal);
        var expectedEnd = DateTime.Parse(expectedEndText, null, System.Globalization.DateTimeStyles.AdjustToUniversal);
        var result = MachineRequestFirstPeriodPricing.Calculate(activation);

        Assert.AreEqual(expectedEnd, result.FirstPeriodEndUtc);
        Assert.AreEqual(StripeMachineAdditionService.CalculateServiceCents(
            result.CycleStartUtc, result.FirstPeriodEndUtc, activation), result.ServiceAmountCents);
        Assert.IsGreaterThanOrEqualTo(0, result.ServiceAmountCents);
        Assert.IsLessThanOrEqualTo(1990, result.ServiceAmountCents);
    }

    [TestMethod]
    public void ParisBoundariesRespectSummerWinterAndDstChanges()
    {
        var summer = MachineRequestFirstPeriodPricing.Calculate(new DateTime(2026, 7, 15, 10, 0, 0, DateTimeKind.Utc));
        Assert.AreEqual(new DateTime(2026, 6, 30, 22, 0, 0, DateTimeKind.Utc), summer.CycleStartUtc);
        Assert.AreEqual(new DateTime(2026, 7, 31, 22, 0, 0, DateTimeKind.Utc), summer.FirstPeriodEndUtc);

        var winter = MachineRequestFirstPeriodPricing.Calculate(new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc));
        Assert.AreEqual(new DateTime(2025, 12, 31, 23, 0, 0, DateTimeKind.Utc), winter.CycleStartUtc);
        Assert.AreEqual(new DateTime(2026, 1, 31, 23, 0, 0, DateTimeKind.Utc), winter.FirstPeriodEndUtc);

        var springChange = MachineRequestFirstPeriodPricing.Calculate(new DateTime(2026, 3, 29, 0, 30, 0, DateTimeKind.Utc));
        Assert.AreEqual(new DateTime(2026, 2, 28, 23, 0, 0, DateTimeKind.Utc), springChange.CycleStartUtc);
        Assert.AreEqual(new DateTime(2026, 3, 31, 22, 0, 0, DateTimeKind.Utc), springChange.FirstPeriodEndUtc);

        var autumnChange = MachineRequestFirstPeriodPricing.Calculate(new DateTime(2026, 10, 25, 1, 30, 0, DateTimeKind.Utc));
        Assert.AreEqual(new DateTime(2026, 9, 30, 22, 0, 0, DateTimeKind.Utc), autumnChange.CycleStartUtc);
        Assert.AreEqual(new DateTime(2026, 10, 31, 23, 0, 0, DateTimeKind.Utc), autumnChange.FirstPeriodEndUtc);
    }

    [TestMethod]
    public void FirstPeriodRejectsNonUtcActivation()
    {
        Assert.ThrowsExactly<ArgumentException>(() => MachineRequestFirstPeriodPricing.Calculate(
            new DateTime(2026, 9, 22, 10, 0, 0, DateTimeKind.Unspecified)));
    }

    [TestMethod]
    [DataRow((int)MachineRequestPaymentStatus.Pending)]
    [DataRow((int)MachineRequestPaymentStatus.Authorized)]
    [DataRow((int)MachineRequestPaymentStatus.Cancelled)]
    public async Task BusinessEntityAttachmentRejectsNonCapturedPayments(int statusValue)
    {
        var setup = await BusinessSetup((MachineRequestPaymentStatus)statusValue);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => setup.Service.AttachBusinessEntitiesAsync(
            setup.PaymentId, setup.CompanyA, setup.MachineA, default));
    }

    [TestMethod]
    public async Task BusinessEntityAttachmentPersistsOnceWithoutChangingFinancialDataOrCreatingAnything()
    {
        var setup = await BusinessSetup(MachineRequestPaymentStatus.Captured);
        var before = await setup.Db.MachineRequestPayments.AsNoTracking().SingleAsync(item => item.Id == setup.PaymentId);

        var first = await setup.Service.AttachBusinessEntitiesAsync(setup.PaymentId, setup.CompanyA, setup.MachineA, default);
        var retry = await setup.Service.AttachBusinessEntitiesAsync(setup.PaymentId, setup.CompanyA, setup.MachineA, default);

        Assert.AreEqual("captured", first!.Status);
        Assert.AreEqual("businessEntitiesCreated", first.ProvisioningStage);
        Assert.AreEqual(setup.CompanyA, first.CompanyId);
        Assert.AreEqual(setup.MachineA, first.MachineId);
        Assert.AreEqual(first.CompanyId, retry!.CompanyId);
        Assert.AreEqual(first.MachineId, retry.MachineId);
        var after = await setup.Db.MachineRequestPayments.AsNoTracking().SingleAsync(item => item.Id == setup.PaymentId);
        Assert.AreEqual(before.ActivatedAtUtc, after.ActivatedAtUtc);
        Assert.AreEqual(before.FirstPeriodEndUtc, after.FirstPeriodEndUtc);
        Assert.AreEqual(before.ServiceAmountCents, after.ServiceAmountCents);
        Assert.AreEqual(before.FinalCaptureAmountCents, after.FinalCaptureAmountCents);
        Assert.HasCount(2, await setup.Db.Companies.ToListAsync());
        Assert.HasCount(2, await setup.Db.Machines.ToListAsync());
        Assert.HasCount(0, await setup.Db.BillingAccounts.ToListAsync());
        Assert.HasCount(0, await setup.Db.MachineBillingPeriods.ToListAsync());
        Assert.HasCount(0, await setup.Db.CompanyWallets.ToListAsync());
    }

    [TestMethod]
    public async Task BusinessEntityAttachmentValidatesCompanyMachineOwnershipAndImmutableDestination()
    {
        var setup = await BusinessSetup(MachineRequestPaymentStatus.Captured);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => setup.Service.AttachBusinessEntitiesAsync(
            setup.PaymentId, Guid.NewGuid(), setup.MachineA, default));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => setup.Service.AttachBusinessEntitiesAsync(
            setup.PaymentId, setup.CompanyA, Guid.NewGuid(), default));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => setup.Service.AttachBusinessEntitiesAsync(
            setup.PaymentId, setup.CompanyA, setup.MachineB, default));

        await setup.Service.AttachBusinessEntitiesAsync(setup.PaymentId, setup.CompanyA, setup.MachineA, default);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => setup.Service.AttachBusinessEntitiesAsync(
            setup.PaymentId, setup.CompanyB, setup.MachineB, default));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => setup.Service.AttachBusinessEntitiesAsync(
            setup.PaymentId, setup.CompanyA, setup.MachineB, default));
    }

    [TestMethod]
    public async Task MachineAlreadyUsedByAnotherInitialRequestIsRejectedBeforeUniqueIndex()
    {
        var setup = await BusinessSetup(MachineRequestPaymentStatus.Captured);
        var existing = Payment();
        existing.Status = MachineRequestPaymentStatus.Captured;
        existing.StripePaymentIntentId = "pi_existing";
        existing.AuthorizationEventId = "evt_existing";
        existing.AuthorizedAtUtc = existing.CreatedAtUtc;
        existing.CapturedAtUtc = existing.CreatedAtUtc;
        existing.MachineId = setup.MachineA;
        existing.CompanyId = setup.CompanyA;
        existing.ProvisioningStage = MachineRequestProvisioningStage.BusinessEntitiesCreated;
        setup.Db.MachineRequestPayments.Add(existing);
        await setup.Db.SaveChangesAsync();

        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => setup.Service.AttachBusinessEntitiesAsync(
            setup.PaymentId, setup.CompanyA, setup.MachineA, default));
        StringAssert.Contains(error.Message, "déjà rattachée");
    }

    [TestMethod]
    public async Task HistoricalCapturedPaymentIsNotForcedIntoNewProvisioning()
    {
        var setup = await BusinessSetup(MachineRequestPaymentStatus.Captured, newWorkflow: false);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => setup.Service.AttachBusinessEntitiesAsync(
            setup.PaymentId, setup.CompanyA, setup.MachineA, default));
        var current = await setup.Db.MachineRequestPayments.AsNoTracking().SingleAsync(item => item.Id == setup.PaymentId);
        Assert.AreEqual(MachineRequestProvisioningStage.AwaitingAcceptance, current.ProvisioningStage);
        Assert.IsNull(current.CompanyId);
        Assert.IsNull(current.MachineId);
    }

    private static async Task<(DiagLinkDbContext Db, MachineRequestPaymentService Service, Guid PaymentId,
        Guid CompanyA, Guid CompanyB, Guid MachineA, Guid MachineB)> BusinessSetup(
        MachineRequestPaymentStatus status, bool newWorkflow = true)
    {
        var db = new DiagLinkDbContext(new DbContextOptionsBuilder<DiagLinkDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var companyA = Guid.NewGuid(); var companyB = Guid.NewGuid();
        var machineA = Guid.NewGuid(); var machineB = Guid.NewGuid(); var paymentId = Guid.NewGuid();
        var now = new DateTime(2026, 9, 22, 14, 37, 0, DateTimeKind.Utc);
        db.Companies.AddRange(
            new Company { Id = companyA, Name = "Atelier A", Status = "active", CreatedAtUtc = now, UpdatedAtUtc = now },
            new Company { Id = companyB, Name = "Atelier B", Status = "active", CreatedAtUtc = now, UpdatedAtUtc = now });
        db.Machines.AddRange(
            new Machine { Id = machineA, CompanyId = companyA, Name = "Machine A", Status = "active", CreatedAtUtc = now, UpdatedAtUtc = now },
            new Machine { Id = machineB, CompanyId = companyB, Name = "Machine B", Status = "active", CreatedAtUtc = now, UpdatedAtUtc = now });
        db.MachineRequestPayments.Add(new PaymentEntity
        {
            Id = paymentId, Status = status, EstimatedTotalPages = 400,
            AmountCents = newWorkflow ? 12980 : 9990, Currency = "EUR", StripeSessionId = "cs_test",
            StripePaymentIntentId = status == MachineRequestPaymentStatus.Pending ? null : "pi_test",
            AuthorizationEventId = status == MachineRequestPaymentStatus.Pending ? null : "evt_test",
            CreatedAtUtc = now, UpdatedAtUtc = now,
            AuthorizedAtUtc = status == MachineRequestPaymentStatus.Pending ? null : now,
            CapturedAtUtc = status == MachineRequestPaymentStatus.Captured ? now : null,
            CancelledAtUtc = status == MachineRequestPaymentStatus.Cancelled ? now : null,
            ActivatedAtUtc = newWorkflow ? now : null,
            FirstPeriodEndUtc = newWorkflow ? new DateTime(2026, 9, 30, 22, 0, 0, DateTimeKind.Utc) : null,
            ServiceAmountCents = newWorkflow ? 500 : null,
            FinalCaptureAmountCents = newWorkflow ? 11490 : null,
            ProvisioningStage = newWorkflow ? MachineRequestProvisioningStage.AmountFinalized : MachineRequestProvisioningStage.AwaitingAcceptance
        });
        await db.SaveChangesAsync();
        return (db, new MachineRequestPaymentService(new MachineRequestPaymentStore(db), new ForbiddenGateway()),
            paymentId, companyA, companyB, machineA, machineB);
    }

    private sealed class ForbiddenGateway : IMachineRequestPaymentGateway
    {
        private static Task<T> Forbidden<T>() => Task.FromException<T>(new AssertFailedException("Aucune opération Stripe ne doit être appelée."));
        public Task<MachineRequestCheckout> CreateAsync(WebApp.Api.Services.MachineRequestPayment payment, CancellationToken ct) => Forbidden<MachineRequestCheckout>();
        public Task<MachineRequestPaymentProof> ReadAsync(WebApp.Api.Services.MachineRequestPayment payment, string sessionId, CancellationToken ct) => Forbidden<MachineRequestPaymentProof>();
        public Task<MachineRequestPaymentProof> CaptureAsync(WebApp.Api.Services.MachineRequestPayment payment, CancellationToken ct) => Forbidden<MachineRequestPaymentProof>();
        public Task<MachineRequestPaymentProof> CancelAsync(WebApp.Api.Services.MachineRequestPayment payment, CancellationToken ct) => Forbidden<MachineRequestPaymentProof>();
    }

    private static async Task AssertConstraintFailure(int serviceAmount, long finalCaptureAmount)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<DiagLinkDbContext>().UseSqlite(connection).Options;
        await using var db = new DiagLinkDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var entity = Payment();
        entity.ServiceAmountCents = serviceAmount;
        entity.FinalCaptureAmountCents = finalCaptureAmount;
        db.MachineRequestPayments.Add(entity);
        await Assert.ThrowsExactlyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private static PaymentEntity Payment() => new()
    {
        Id = Guid.NewGuid(), Status = MachineRequestPaymentStatus.Pending, EstimatedTotalPages = 400,
        AmountCents = 12980, Currency = "EUR", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
    };

    private static PaymentEntity WithDates(PaymentEntity payment, DateTime start, DateTime end)
    {
        payment.ActivatedAtUtc = start;
        payment.FirstPeriodEndUtc = end;
        return payment;
    }
}
