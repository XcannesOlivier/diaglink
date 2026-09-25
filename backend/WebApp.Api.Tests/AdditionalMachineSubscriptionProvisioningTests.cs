using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Data;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

[TestClass]
public sealed class AdditionalMachineSubscriptionProvisioningTests
{
    [TestMethod]
    public async Task ExistingCustomerAndSubscriptionAdvanceToSubscriptionCreatedWithoutNewObjects()
    {
        await using var fixture = await Fixture.CreateAsync();

        var linked = await fixture.CustomerService.LinkAsync(fixture.PaymentId, default);
        var configured = await fixture.SubscriptionService.ConfigureAsync(fixture.PaymentId, default);
        var retry = await fixture.SubscriptionService.ConfigureAsync(fixture.PaymentId, default);

        Assert.AreEqual(MachineRequestProvisioningStage.CustomerLinked, linked!.ProvisioningStage);
        Assert.AreEqual(MachineRequestProvisioningStage.SubscriptionCreated, configured!.ProvisioningStage);
        Assert.AreEqual(MachineRequestProvisioningStage.SubscriptionCreated, retry!.ProvisioningStage);
        Assert.AreEqual(1, fixture.CustomerGateway.ExistingReads);
        Assert.AreEqual(0, fixture.CustomerGateway.InitialReads);
        Assert.AreEqual(0, fixture.CustomerGateway.Configurations);
        Assert.AreEqual(2, fixture.SubscriptionGateway.TargetQuantity);
        Assert.AreEqual(2, fixture.SubscriptionGateway.AdditionalReads);
        Assert.AreEqual(2, fixture.SubscriptionGateway.QuantityWrites);
        Assert.AreEqual(1, fixture.SubscriptionGateway.Mutations);
        Assert.AreEqual(0, fixture.SubscriptionGateway.Creates);
        Assert.AreEqual(0, await fixture.Db.MachineBillingPeriods.CountAsync());
        Assert.AreEqual(0, await fixture.Db.CompanyWallets.CountAsync());
        Assert.AreEqual(0, await fixture.Db.StripeMachineAdditions.CountAsync());
        Assert.AreEqual(MachineRequestPaymentStatus.Captured,
            (await fixture.Db.MachineRequestPayments.SingleAsync()).Status);
    }

    [TestMethod]
    public async Task MissingExistingSubscriptionStopsBeforeAnyStripeMutation()
    {
        await using var fixture = await Fixture.CreateAsync(subscriptionId: null);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => fixture.CustomerService.LinkAsync(fixture.PaymentId, default));

        Assert.AreEqual(0, fixture.SubscriptionGateway.QuantityWrites);
        Assert.AreEqual(0, fixture.SubscriptionGateway.Creates);
        Assert.AreEqual(MachineRequestProvisioningStage.BusinessEntitiesCreated,
            (await fixture.Db.MachineRequestPayments.SingleAsync()).ProvisioningStage);
    }

    [TestMethod]
    public async Task PaymentIntentCustomerMismatchDoesNotLinkOrMutateCustomer()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.CustomerGateway.RejectExistingCustomer = true;

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => fixture.CustomerService.LinkAsync(fixture.PaymentId, default));

        Assert.AreEqual(0, fixture.CustomerGateway.Configurations);
        Assert.AreEqual(MachineRequestProvisioningStage.BusinessEntitiesCreated,
            (await fixture.Db.MachineRequestPayments.SingleAsync()).ProvisioningStage);
    }

    [TestMethod]
    public async Task CrashAfterQuantityUpdateIsRecoveredWithoutCreatingSubscription()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.CustomerService.LinkAsync(fixture.PaymentId, default);
        fixture.SubscriptionGateway.ThrowAfterFirstQuantityWrite = true;

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => fixture.SubscriptionService.ConfigureAsync(fixture.PaymentId, default));
        var recovered = await fixture.SubscriptionService.ConfigureAsync(fixture.PaymentId, default);

        Assert.AreEqual(MachineRequestProvisioningStage.SubscriptionCreated, recovered!.ProvisioningStage);
        Assert.AreEqual(2, fixture.SubscriptionGateway.QuantityWrites);
        Assert.AreEqual(1, fixture.SubscriptionGateway.Mutations);
        Assert.AreEqual(0, fixture.SubscriptionGateway.Creates);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public DiagLinkDbContext Db { get; }
        public Guid PaymentId { get; } = Guid.NewGuid();
        public CustomerGateway CustomerGateway { get; } = new();
        public SubscriptionGateway SubscriptionGateway { get; } = new();
        public MachineRequestCustomerLinkService CustomerService { get; private set; } = null!;
        public MachineRequestSubscriptionService SubscriptionService { get; private set; } = null!;

        private Fixture()
        {
            var options = new DbContextOptionsBuilder<DiagLinkDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options;
            Db = new DiagLinkDbContext(options);
            var store = new MachineRequestPaymentStore(Db);
            var resolver = new AdditionalMachinePaymentContextResolver(Db, new DiagLinkUserLookupService(Db));
            var billing = new StripeBillingService(options, new ForbiddenBillingGateway(), Settings);
            CustomerService = new(store, billing, CustomerGateway, resolver);
            SubscriptionService = new(options, store, billing, SubscriptionGateway, resolver);
        }

        public static async Task<Fixture> CreateAsync(string? subscriptionId = "sub_existing")
        {
            var fixture = new Fixture();
            var companyId = Guid.NewGuid(); var userId = Guid.NewGuid(); var now = DateTime.UtcNow;
            fixture.Db.Companies.Add(new Company { Id = companyId, Name = "Company", Status = "active", CreatedAtUtc = now, UpdatedAtUtc = now });
            fixture.Db.Users.Add(new User { Id = userId, CompanyId = companyId, Email = "admin@test", Role = DiagLinkRoles.CompanyAdmin, Status = "active", CreatedAt = now, UpdatedAt = now });
            fixture.Db.Machines.AddRange(
                new Machine { Id = Guid.NewGuid(), CompanyId = companyId, Name = "Existing", Status = "active" },
                new Machine { Id = Guid.NewGuid(), CompanyId = companyId, Name = "Added", Status = "active" });
            fixture.Db.BillingAccounts.Add(new BillingAccount { Id = Guid.NewGuid(), CompanyId = companyId,
                StripeCustomerId = "cus_existing", StripeSubscriptionId = subscriptionId,
                SubscriptionStatus = "active", CreatedAtUtc = now, UpdatedAtUtc = now });
            fixture.Db.MachineRequestPayments.Add(new WebApp.Api.Models.Entities.MachineRequestPayment
            {
                Id = fixture.PaymentId, RequestKind = MachineRequestKind.AdditionalMachine,
                RequestedByUserId = userId, CompanyId = companyId, MachineId = fixture.Db.Machines.Local.Last().Id,
                Status = MachineRequestPaymentStatus.Captured, EstimatedTotalPages = 416, AmountCents = 13412,
                Currency = "EUR", StripeSessionId = "cs_test", StripePaymentIntentId = "pi_test",
                AuthorizationEventId = "evt_test", MachineRequestId = "request", RequestLinkedAtUtc = now,
                ActivatedAtUtc = now, FirstPeriodEndUtc = new DateTime(2026, 9, 30, 22, 0, 0, DateTimeKind.Utc),
                ServiceAmountCents = 495, FinalCaptureAmountCents = 11917, CreatedAtUtc = now,
                UpdatedAtUtc = now, AuthorizedAtUtc = now, CapturedAtUtc = now,
                ProvisioningStage = MachineRequestProvisioningStage.BusinessEntitiesCreated
            });
            await fixture.Db.SaveChangesAsync();
            return fixture;
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class CustomerGateway : IMachineRequestCustomerLinkGateway
    {
        public int InitialReads, ExistingReads, Configurations;
        public bool RejectExistingCustomer;
        public Task<MachineRequestStripeIdentity> ReadAndValidateAsync(WebApp.Api.Services.MachineRequestPayment payment, CancellationToken ct)
        { InitialReads++; return Task.FromResult(new MachineRequestStripeIdentity("cus_checkout", "pm_checkout")); }
        public Task<MachineRequestStripeIdentity> ReadAndValidateExistingCustomerAsync(WebApp.Api.Services.MachineRequestPayment payment, string expectedCustomerId, CancellationToken ct)
        { ExistingReads++; return RejectExistingCustomer ? Task.FromException<MachineRequestStripeIdentity>(new InvalidOperationException("Customer mismatch.")) : Task.FromResult(new MachineRequestStripeIdentity(expectedCustomerId, "pm_checkout")); }
        public Task ConfirmCustomerConfigurationAsync(WebApp.Api.Services.MachineRequestPayment payment, Guid companyId, MachineRequestStripeIdentity identity, CancellationToken ct)
        { Configurations++; return Task.CompletedTask; }
    }

    private sealed class SubscriptionGateway : IMachineRequestSubscriptionGateway
    {
        public int AdditionalReads, QuantityWrites, Mutations, Creates, TargetQuantity;
        private int currentQuantity = 1;
        public bool ThrowAfterFirstQuantityWrite;
        public Task ValidatePriceAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<MachineRequestSubscriptionContext> ReadContextAsync(WebApp.Api.Services.MachineRequestPayment payment, string customerId, string? subscriptionId, int targetQuantity, CancellationToken ct) => throw new AssertFailedException("CAS A context forbidden.");
        public Task<MachineRequestSubscriptionContext> ReadAdditionalContextAsync(WebApp.Api.Services.MachineRequestPayment payment, string customerId, string subscriptionId, int targetQuantity, CancellationToken ct)
        { AdditionalReads++; TargetQuantity = targetQuantity; return Task.FromResult(new MachineRequestSubscriptionContext(customerId, "pm_recurring", subscriptionId, targetQuantity)); }
        public Task<StripeSubscriptionSnapshot> CreateAsync(WebApp.Api.Services.MachineRequestPayment payment, MachineRequestSubscriptionContext context, DateTime firstPeriodEndUtc, CancellationToken ct)
        { Creates++; throw new AssertFailedException("No Subscription may be created."); }
        public Task<StripeSubscriptionSnapshot> ReadAsync(WebApp.Api.Services.MachineRequestPayment payment, MachineRequestSubscriptionContext context, CancellationToken ct) => Task.FromResult(Snapshot(context));
        public Task<StripeSubscriptionSnapshot> SetQuantityAsync(WebApp.Api.Services.MachineRequestPayment payment, MachineRequestSubscriptionContext context, CancellationToken ct)
        {
            QuantityWrites++;
            if (currentQuantity == context.TargetQuantity - 1) { currentQuantity = context.TargetQuantity; Mutations++; }
            else if (currentQuantity != context.TargetQuantity) throw new InvalidOperationException("Unexpected quantity.");
            if (ThrowAfterFirstQuantityWrite && QuantityWrites == 1) throw new InvalidOperationException("Simulated crash after Stripe update.");
            return Task.FromResult(Snapshot(context));
        }
        private static StripeSubscriptionSnapshot Snapshot(MachineRequestSubscriptionContext context) =>
            new(context.SubscriptionId!, context.CustomerId, "active", DateTime.UtcNow,
                new DateTime(2026, 9, 30, 22, 0, 0, DateTimeKind.Utc), context.TargetQuantity) { ItemId = "si_existing" };
    }

    private static StripeBillingOptions Settings => new() { Enabled = true, SecretKey = "sk_test_local", PriceId = "price_local" };
    private sealed class ForbiddenBillingGateway : IStripeBillingGateway
    {
        private static Task<T> Forbidden<T>() => Task.FromException<T>(new AssertFailedException("Generic Stripe creation is forbidden."));
        public Task ValidatePriceAsync(CancellationToken ct) => Task.FromException(new AssertFailedException());
        public Task<string> CreateCustomerAsync(Guid companyId, Guid accountId, CancellationToken ct) => Forbidden<string>();
        public Task<string> GetCustomerAsync(string customerId, Guid companyId, CancellationToken ct) => Forbidden<string>();
        public Task<StripeSubscriptionSnapshot> CreateSubscriptionAsync(string customerId, Guid companyId, Guid accountId, int quantity, CancellationToken ct) => Forbidden<StripeSubscriptionSnapshot>();
        public Task<StripeSubscriptionSnapshot> GetSubscriptionAsync(string subscriptionId, string customerId, Guid companyId, CancellationToken ct) => Forbidden<StripeSubscriptionSnapshot>();
        public Task PrepareInitialPaymentAsync(string subscriptionId, string customerId, Guid companyId, CancellationToken ct) => Task.FromException(new AssertFailedException());
    }
}
