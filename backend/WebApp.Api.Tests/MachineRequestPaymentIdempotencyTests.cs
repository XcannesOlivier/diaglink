using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Stripe;
using WebApp.Api.Data;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

[TestClass]
public sealed class MachineRequestPaymentIdempotencyTests
{
    private const string Route = "/api/public/machine-request-payments";

    [TestMethod]
    public async Task Create_NewKey_CreatesOneLocalAndLogicalStripeOperation()
    {
        await using var factory = await PaymentApplicationFactory.CreateAsync();
        var key = Guid.NewGuid();

        var result = await CreateAsync(factory, key, 400, "client@example.test");

        Assert.AreEqual(key, result.PaymentRequestId);
        Assert.AreEqual(1, await factory.CountPaymentsAsync());
        Assert.AreEqual(1, factory.Gateway.CreateAttempts);
        Assert.AreEqual(1, factory.Gateway.LogicalResourceCount);
    }

    [TestMethod]
    public async Task Create_SameKeySequentially_ReusesPaymentAndStripeOperation()
    {
        await using var factory = await PaymentApplicationFactory.CreateAsync();
        var key = Guid.NewGuid();

        var first = await CreateAsync(factory, key, 400, "client@example.test");
        var second = await CreateAsync(factory, key, 400, "client@example.test");

        Assert.AreEqual(first.PaymentRequestId, second.PaymentRequestId);
        Assert.AreEqual(key, second.PaymentRequestId);
        Assert.AreEqual(1, await factory.CountPaymentsAsync());
        Assert.AreEqual(2, factory.Gateway.CreateAttempts);
        Assert.AreEqual(1, factory.Gateway.LogicalResourceCount);
    }

    [TestMethod]
    public async Task Create_ResponseLostThenRetried_ReusesPaymentAndStripeOperation()
    {
        await using var factory = await PaymentApplicationFactory.CreateAsync();
        var key = Guid.NewGuid();

        using (var lostResponse = await PostAsync(factory, key, 400, "client@example.test"))
        {
            Assert.AreEqual(HttpStatusCode.OK, lostResponse.StatusCode);
        }
        var retry = await CreateAsync(factory, key, 400, "client@example.test");

        Assert.AreEqual(key, retry.PaymentRequestId);
        Assert.AreEqual(1, await factory.CountPaymentsAsync());
        Assert.AreEqual(1, factory.Gateway.LogicalResourceCount);
    }

    [TestMethod]
    public async Task Create_SameKeyConcurrently_CreatesOneLocalAndLogicalStripeOperation()
    {
        await using var factory = await PaymentApplicationFactory.CreateAsync(synchronizeGatewayCalls: true);
        var key = Guid.NewGuid();

        var responses = await Task.WhenAll(
            CreateAsync(factory, key, 400, "client@example.test"),
            CreateAsync(factory, key, 400, "client@example.test"));

        Assert.IsTrue(responses.All(response => response.PaymentRequestId == key));
        Assert.AreEqual(1, await factory.CountPaymentsAsync());
        Assert.AreEqual(2, factory.Gateway.CreateAttempts);
        Assert.IsTrue(factory.Gateway.AttemptedPaymentIds.All(id => id == key));
        Assert.AreEqual(1, factory.Gateway.LogicalResourceCount);
    }

    [TestMethod]
    public async Task Create_DifferentKeys_CreateIndependentOperations()
    {
        await using var factory = await PaymentApplicationFactory.CreateAsync();
        var firstKey = Guid.NewGuid();
        var secondKey = Guid.NewGuid();

        var first = await CreateAsync(factory, firstKey, 400, "client@example.test");
        var second = await CreateAsync(factory, secondKey, 400, "client@example.test");

        Assert.AreEqual(firstKey, first.PaymentRequestId);
        Assert.AreEqual(secondKey, second.PaymentRequestId);
        Assert.AreNotEqual(first.PaymentRequestId, second.PaymentRequestId);
        Assert.AreEqual(2, await factory.CountPaymentsAsync());
        Assert.AreEqual(2, factory.Gateway.LogicalResourceCount);
    }

    [TestMethod]
    public async Task Create_SameKeyWithDifferentParameters_ReturnsConflictWithoutStripeCall()
    {
        await using var factory = await PaymentApplicationFactory.CreateAsync();
        var key = Guid.NewGuid();
        _ = await CreateAsync(factory, key, 400, "client@example.test");

        using var conflict = await PostAsync(factory, key, 550, "other@example.test");

        Assert.AreEqual(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.AreEqual(1, await factory.CountPaymentsAsync());
        Assert.AreEqual(1, factory.Gateway.CreateAttempts);
        Assert.AreEqual(1, factory.Gateway.LogicalResourceCount);
    }

    [TestMethod]
    public async Task Create_AmbiguousStripeFailure_RetryConvergesWithoutDeletingLocalPayment()
    {
        await using var factory = await PaymentApplicationFactory.CreateAsync(FailureMode.AmbiguousFirstAttempt);
        var key = Guid.NewGuid();

        using var failed = await PostAsync(factory, key, 400, "client@example.test");
        Assert.AreEqual(HttpStatusCode.BadGateway, failed.StatusCode);
        Assert.AreEqual(1, await factory.CountPaymentsAsync());

        var retry = await CreateAsync(factory, key, 400, "client@example.test");

        Assert.AreEqual(key, retry.PaymentRequestId);
        Assert.AreEqual(1, await factory.CountPaymentsAsync());
        Assert.AreEqual(2, factory.Gateway.CreateAttempts);
        Assert.AreEqual(1, factory.Gateway.LogicalResourceCount);
    }

    [TestMethod]
    public async Task Create_CertainPreCreationFailure_RetryUsesSameLocalPayment()
    {
        await using var factory = await PaymentApplicationFactory.CreateAsync(FailureMode.CertainBeforeCreation);
        var key = Guid.NewGuid();

        using var failed = await PostAsync(factory, key, 400, "client@example.test");
        Assert.AreEqual(HttpStatusCode.BadGateway, failed.StatusCode);
        Assert.AreEqual(1, await factory.CountPaymentsAsync());
        Assert.AreEqual(0, factory.Gateway.LogicalResourceCount);

        var retry = await CreateAsync(factory, key, 400, "client@example.test");

        Assert.AreEqual(key, retry.PaymentRequestId);
        Assert.AreEqual(1, await factory.CountPaymentsAsync());
        Assert.AreEqual(1, factory.Gateway.LogicalResourceCount);
    }

    [TestMethod]
    public async Task Create_MissingOrInvalidKey_IsRejectedBeforePersistence()
    {
        await using var factory = await PaymentApplicationFactory.CreateAsync();
        using var client = factory.CreateClient();

        using var missing = await client.PostAsJsonAsync(Route,
            new CreateMachineRequestPayment(400, "client@example.test"));
        using var invalidRequest = new HttpRequestMessage(HttpMethod.Post, Route)
        {
            Content = JsonContent.Create(new CreateMachineRequestPayment(400, "client@example.test")),
        };
        invalidRequest.Headers.Add("Idempotency-Key", "not-a-uuid");
        using var invalid = await client.SendAsync(invalidRequest);

        Assert.AreEqual(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.AreEqual(0, await factory.CountPaymentsAsync());
        Assert.AreEqual(0, factory.Gateway.CreateAttempts);
    }

    private static async Task<MachineRequestPaymentResult> CreateAsync(
        PaymentApplicationFactory factory,
        Guid idempotencyKey,
        int totalPages,
        string email)
    {
        using var response = await PostAsync(factory, idempotencyKey, totalPages, email);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MachineRequestPaymentResult>();
        Assert.IsNotNull(body);
        return body;
    }

    private static async Task<HttpResponseMessage> PostAsync(
        PaymentApplicationFactory factory,
        Guid idempotencyKey,
        int totalPages,
        string email)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Post, Route)
        {
            Content = JsonContent.Create(new CreateMachineRequestPayment(totalPages, email)),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey.ToString());
        return await client.SendAsync(request);
    }

    private enum FailureMode
    {
        None,
        AmbiguousFirstAttempt,
        CertainBeforeCreation,
    }

    private sealed class IdempotentFakeGateway(FailureMode failureMode, bool synchronizeCalls)
        : IMachineRequestPaymentGateway
    {
        private readonly ConcurrentDictionary<Guid, MachineRequestCheckout> resources = new();
        private readonly ConcurrentQueue<Guid> attemptedPaymentIds = new();
        private readonly TaskCompletionSource concurrentCalls = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int createAttempts;

        public int CreateAttempts => createAttempts;
        public int LogicalResourceCount => resources.Count;
        public IReadOnlyCollection<Guid> AttemptedPaymentIds => attemptedPaymentIds.ToArray();

        public async Task<MachineRequestCheckout> CreateAsync(MachineRequestPayment payment, CancellationToken ct)
        {
            var attempt = Interlocked.Increment(ref createAttempts);
            attemptedPaymentIds.Enqueue(payment.PaymentRequestId);

            if (synchronizeCalls)
            {
                if (attempt == 2) concurrentCalls.TrySetResult();
                await concurrentCalls.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
            }

            if (failureMode == FailureMode.CertainBeforeCreation && attempt == 1)
                throw new StripeException("Certain failure before remote creation.");

            var checkout = resources.GetOrAdd(payment.PaymentRequestId, id =>
                new MachineRequestCheckout($"cs_{id:N}", $"https://checkout.stripe.test/{id:N}"));

            if (failureMode == FailureMode.AmbiguousFirstAttempt && attempt == 1)
                throw new StripeException("Ambiguous failure after remote creation.");

            return checkout;
        }

        public Task<MachineRequestPaymentProof> ReadAsync(
            MachineRequestPayment payment, string sessionId, CancellationToken ct) =>
            throw new AssertFailedException("Idempotency tests must not verify Stripe payments.");
        public Task<MachineRequestPaymentProof> CaptureAsync(MachineRequestPayment payment, CancellationToken ct) =>
            throw new AssertFailedException("Idempotency tests must not capture Stripe payments.");
        public Task<MachineRequestPaymentProof> CancelAsync(MachineRequestPayment payment, CancellationToken ct) =>
            throw new AssertFailedException("Idempotency tests must not cancel Stripe payments.");
    }

    private sealed class PaymentApplicationFactory : WebApplicationFactory<BlobStorageService>
    {
        private readonly string connectionString;
        private readonly SqliteConnection keeper;

        private PaymentApplicationFactory(FailureMode failureMode, bool synchronizeGatewayCalls)
        {
            connectionString = $"Data Source=payment-idempotency-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Default Timeout=30";
            keeper = new SqliteConnection(connectionString);
            keeper.Open();
            Gateway = new IdempotentFakeGateway(failureMode, synchronizeGatewayCalls);
        }

        public IdempotentFakeGateway Gateway { get; }

        public static async Task<PaymentApplicationFactory> CreateAsync(
            FailureMode failureMode = FailureMode.None,
            bool synchronizeGatewayCalls = false)
        {
            var factory = new PaymentApplicationFactory(failureMode, synchronizeGatewayCalls);
            try
            {
                await factory.InitializeDatabaseAsync();
                return factory;
            }
            catch
            {
                await factory.DisposeAsync();
                throw;
            }
        }

        public async Task<int> CountPaymentsAsync()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiagLinkDbContext>();
            return await db.MachineRequestPayments.CountAsync();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["AZURE_STORAGE_CONNECTION_STRING"] = string.Empty }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveWindowsEventLogProvider();
                services.RemoveAll<DiagLinkDbContext>();
                services.RemoveAll<DbContextOptions<DiagLinkDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<DiagLinkDbContext>>();
                services.RemoveAll<IMachineRequestPaymentGateway>();
                services.RemoveAll<BlobStorageService>();
                services.AddDbContext<DiagLinkDbContext>(options => options.UseSqlite(connectionString));
                services.AddSingleton<IMachineRequestPaymentGateway>(Gateway);
                services.AddSingleton<IMachineRequestBlobClient, UnusedMachineRequestBlobClient>();
                services.AddScoped<MachineRequestStorageService>();
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) keeper.Dispose();
        }

        private async Task InitializeDatabaseAsync()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiagLinkDbContext>();
            await db.Database.EnsureCreatedAsync();
            await db.Database.ExecuteSqlRawAsync("""
                DROP TABLE MachineRequestPayments;
                CREATE TABLE MachineRequestPayments (
                    Id TEXT NOT NULL PRIMARY KEY,
                    RequestKind INTEGER NOT NULL DEFAULT 0,
                    RequestedByUserId TEXT NULL,
                    Status INTEGER NOT NULL,
                    EstimatedTotalPages INTEGER NOT NULL,
                    AmountCents INTEGER NOT NULL,
                    Currency TEXT NOT NULL,
                    Email TEXT NULL,
                    StripeSessionId TEXT NULL,
                    StripePaymentIntentId TEXT NULL,
                    AuthorizationEventId TEXT NULL,
                    MachineRequestId TEXT NULL,
                    CreatedAtUtc TEXT NOT NULL,
                    UpdatedAtUtc TEXT NOT NULL,
                    AuthorizedAtUtc TEXT NULL,
                    CapturedAtUtc TEXT NULL,
                    CancelledAtUtc TEXT NULL,
                    RequestLinkedAtUtc TEXT NULL,
                    ActivatedAtUtc TEXT NULL,
                    FirstPeriodEndUtc TEXT NULL,
                    ServiceAmountCents INTEGER NULL,
                    FinalCaptureAmountCents INTEGER NULL,
                    CompanyId TEXT NULL,
                    MachineId TEXT NULL,
                    TargetMachineId TEXT NULL,
                    ProvisioningStage INTEGER NOT NULL,
                    ProvisioningCompletedAtUtc TEXT NULL,
                    PreparationStatus INTEGER NOT NULL DEFAULT 0,
                    ReadyAtUtc TEXT NULL,
                    ReadyByUserId TEXT NULL,
                    RowVersion BLOB NOT NULL DEFAULT X'00'
                );
                CREATE UNIQUE INDEX IX_MachineRequestPayments_StripeSessionId
                    ON MachineRequestPayments (StripeSessionId) WHERE StripeSessionId IS NOT NULL;
                CREATE UNIQUE INDEX IX_MachineRequestPayments_StripePaymentIntentId
                    ON MachineRequestPayments (StripePaymentIntentId) WHERE StripePaymentIntentId IS NOT NULL;
                CREATE UNIQUE INDEX IX_MachineRequestPayments_AuthorizationEventId
                    ON MachineRequestPayments (AuthorizationEventId) WHERE AuthorizationEventId IS NOT NULL;
                CREATE UNIQUE INDEX IX_MachineRequestPayments_MachineRequestId
                    ON MachineRequestPayments (MachineRequestId) WHERE MachineRequestId IS NOT NULL;
                """);
        }
    }

    private sealed class UnusedMachineRequestBlobClient : IMachineRequestBlobClient
    {
        public Task EnsurePrivateContainerAsync(CancellationToken cancellationToken = default) =>
            throw new AssertFailedException("Payment idempotency tests must not access Blob storage.");
        public Task UploadAsync(string blobName, Stream content, string contentType, bool overwrite,
            CancellationToken cancellationToken = default) =>
            throw new AssertFailedException("Payment idempotency tests must not access Blob storage.");
        public Task<Stream?> OpenReadAsync(string blobName, CancellationToken cancellationToken = default) =>
            throw new AssertFailedException("Payment idempotency tests must not access Blob storage.");
        public IAsyncEnumerable<string> ListNamesAsync(string? prefix = null,
            CancellationToken cancellationToken = default) =>
            throw new AssertFailedException("Payment idempotency tests must not access Blob storage.");
        public Task DeletePrefixAsync(string prefix, CancellationToken cancellationToken = default) =>
            throw new AssertFailedException("Payment idempotency tests must not access Blob storage.");
    }
}
