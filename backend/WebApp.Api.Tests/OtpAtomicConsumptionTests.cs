using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Data;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

[TestClass]
public sealed class OtpAtomicConsumptionTests
{
    private const string OtpPepper = "otp-atomic-consumption-test-pepper";
    private const string VerifyCodeRoute = "/api/auth/verify-code";
    private const string ValidCode = "123456";

    [TestMethod]
    public async Task VerifyCode_ValidOtp_CreatesSession()
    {
        await using var factory = await OtpRelationalApplicationFactory.CreateAsync();
        var userId = await factory.SeedOtpAsync("valid@example.test", ValidCode, DateTime.UtcNow.AddMinutes(10));

        var response = await VerifyAsync(factory, "valid@example.test", ValidCode);

        Assert.IsTrue(response.Success);
        Assert.IsFalse(string.IsNullOrWhiteSpace(response.SessionToken));
        Assert.AreEqual(1, await factory.CountSessionsAsync(userId));
    }

    [TestMethod]
    [DataRow(DiagLinkRoles.SuperAdmin, true)]
    [DataRow(DiagLinkRoles.CompanyAdmin, true)]
    [DataRow(DiagLinkRoles.Technician, false)]
    [DataRow("unexpected_role", false)]
    public async Task VerifyCode_SessionLifetimeDependsOnRole(string role, bool usesMonthlyLifetime)
    {
        await using var factory = await OtpRelationalApplicationFactory.CreateAsync();
        var email = $"{role}@example.test";
        var userId = await factory.SeedOtpAsync(email, ValidCode, DateTime.UtcNow.AddMinutes(10), role);
        var startedAtUtc = DateTime.UtcNow;

        var response = await VerifyAsync(factory, email, ValidCode);

        var completedAtUtc = DateTime.UtcNow;
        Assert.IsTrue(response.Success);
        Assert.IsNotNull(response.ExpiresAtUtc);
        var storedExpiry = await factory.GetSessionExpiryAsync(userId);
        Assert.AreEqual(response.ExpiresAtUtc.Value, storedExpiry);

        var earliestExpected = usesMonthlyLifetime
            ? startedAtUtc.AddMonths(1)
            : startedAtUtc.AddHours(24);
        var latestExpected = usesMonthlyLifetime
            ? completedAtUtc.AddMonths(1)
            : completedAtUtc.AddHours(24);
        Assert.IsTrue(storedExpiry >= earliestExpected && storedExpiry <= latestExpected);
    }

    [TestMethod]
    public async Task ValidateSession_ExpiredSessionRemainsInvalid()
    {
        await using var factory = await OtpRelationalApplicationFactory.CreateAsync();
        var userId = await factory.SeedOtpAsync("expired-session@example.test", ValidCode, DateTime.UtcNow.AddMinutes(10));
        var login = await VerifyAsync(factory, "expired-session@example.test", ValidCode);
        Assert.IsTrue(login.Success);
        Assert.IsNotNull(login.SessionToken);
        await factory.ExpireSessionsAsync(userId);

        var validation = await ValidateSessionAsync(factory, login.SessionToken);

        Assert.IsFalse(validation.Valid);
    }

    [TestMethod]
    public async Task VerifyCode_ReusedOtp_ReturnsGenericFailure()
    {
        await using var factory = await OtpRelationalApplicationFactory.CreateAsync();
        var userId = await factory.SeedOtpAsync("reused@example.test", ValidCode, DateTime.UtcNow.AddMinutes(10));

        var first = await VerifyAsync(factory, "reused@example.test", ValidCode);
        var second = await VerifyAsync(factory, "reused@example.test", ValidCode);

        Assert.IsTrue(first.Success);
        Assert.IsFalse(second.Success);
        Assert.IsNull(second.SessionToken);
        Assert.AreEqual(1, await factory.CountSessionsAsync(userId));
    }

    [TestMethod]
    public async Task VerifyCode_ConcurrentRequests_CreateExactlyOneSession()
    {
        await using var factory = await OtpRelationalApplicationFactory.CreateAsync(synchronizeLoginCodeReads: true);
        var userId = await factory.SeedOtpAsync("concurrent@example.test", ValidCode, DateTime.UtcNow.AddMinutes(10));

        var responses = await Task.WhenAll(
            VerifyAsync(factory, "concurrent@example.test", ValidCode),
            VerifyAsync(factory, "concurrent@example.test", ValidCode));

        Assert.AreEqual(1, await factory.CountSessionsAsync(userId));
        Assert.AreEqual(1, responses.Count(response => response.Success));
        Assert.AreEqual(1, responses.Count(response => !response.Success));
    }

    [TestMethod]
    public async Task VerifyCode_InvalidOtp_ReturnsGenericFailure()
    {
        await using var factory = await OtpRelationalApplicationFactory.CreateAsync();
        var userId = await factory.SeedOtpAsync("invalid@example.test", ValidCode, DateTime.UtcNow.AddMinutes(10));

        var response = await VerifyAsync(factory, "invalid@example.test", "654321");

        Assert.IsFalse(response.Success);
        Assert.IsNull(response.SessionToken);
        Assert.AreEqual(0, await factory.CountSessionsAsync(userId));
    }

    [TestMethod]
    public async Task VerifyCode_ExpiredOtp_ReturnsGenericFailure()
    {
        await using var factory = await OtpRelationalApplicationFactory.CreateAsync();
        var userId = await factory.SeedOtpAsync("expired@example.test", ValidCode, DateTime.UtcNow.AddMinutes(-1));

        var response = await VerifyAsync(factory, "expired@example.test", ValidCode);

        Assert.IsFalse(response.Success);
        Assert.IsNull(response.SessionToken);
        Assert.AreEqual(0, await factory.CountSessionsAsync(userId));
    }

    private static async Task<VerifyCodeResponse> VerifyAsync(
        OtpRelationalApplicationFactory factory,
        string email,
        string code)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var response = await client.PostAsJsonAsync(VerifyCodeRoute, new VerifyCodeRequest
        {
            Email = email,
            Code = code,
        });
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<VerifyCodeResponse>();
        Assert.IsNotNull(body);
        return body;
    }

    private static async Task<ValidateSessionResponse> ValidateSessionAsync(
        OtpRelationalApplicationFactory factory,
        string sessionToken)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var response = await client.PostAsJsonAsync("/api/auth/validate-session", new ValidateSessionRequest
        {
            SessionToken = sessionToken,
        });
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ValidateSessionResponse>();
        Assert.IsNotNull(body);
        return body;
    }

    private sealed class OtpRelationalApplicationFactory : WebApplicationFactory<BlobStorageService>
    {
        private readonly string connectionString;
        private readonly SqliteConnection keeper;
        private readonly LoginCodeReadBarrier? loginCodeReadBarrier;

        private OtpRelationalApplicationFactory(bool synchronizeLoginCodeReads)
        {
            connectionString = $"Data Source=otp-atomic-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Default Timeout=30";
            keeper = new SqliteConnection(connectionString);
            keeper.Open();
            loginCodeReadBarrier = synchronizeLoginCodeReads ? new LoginCodeReadBarrier() : null;
        }

        public static async Task<OtpRelationalApplicationFactory> CreateAsync(
            bool synchronizeLoginCodeReads = false)
        {
            var factory = new OtpRelationalApplicationFactory(synchronizeLoginCodeReads);
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

        public async Task<Guid> SeedOtpAsync(
            string email,
            string code,
            DateTime expiresAtUtc,
            string role = DiagLinkRoles.Technician)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiagLinkDbContext>();
            var now = DateTime.UtcNow;
            var userId = Guid.NewGuid();
            db.Users.Add(new User
            {
                Id = userId,
                CompanyId = Guid.NewGuid(),
                Email = email,
                Role = role,
                Status = "active",
                CreatedAt = now,
                UpdatedAt = now,
            });
            db.LoginCodes.Add(new LoginCode
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                CodeHash = Hash(code),
                CreatedAtUtc = now,
                ExpiresAtUtc = expiresAtUtc,
                UsedAtUtc = null,
            });
            await db.SaveChangesAsync();
            return userId;
        }

        public async Task<int> CountSessionsAsync(Guid userId)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiagLinkDbContext>();
            return await db.UserSessions.CountAsync(session => session.UserId == userId);
        }

        public async Task<DateTime> GetSessionExpiryAsync(Guid userId)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiagLinkDbContext>();
            return await db.UserSessions
                .Where(session => session.UserId == userId)
                .Select(session => session.ExpiresAtUtc)
                .SingleAsync();
        }

        public async Task ExpireSessionsAsync(Guid userId)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiagLinkDbContext>();
            var sessions = await db.UserSessions.Where(session => session.UserId == userId).ToListAsync();
            foreach (var session in sessions) session.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Auth:OtpPepper"] = OtpPepper,
                    ["AZURE_STORAGE_CONNECTION_STRING"] = string.Empty,
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DiagLinkDbContext>();
                services.RemoveAll<DbContextOptions<DiagLinkDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<DiagLinkDbContext>>();
                services.RemoveAll<BlobStorageService>();
                services.AddDbContext<DiagLinkDbContext>(options =>
                {
                    options.UseSqlite(connectionString);
                    if (loginCodeReadBarrier is not null) options.AddInterceptors(loginCodeReadBarrier);
                });
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
                CREATE TABLE IF NOT EXISTS Users (
                    Id TEXT NOT NULL PRIMARY KEY,
                    EntraObjectId TEXT NULL,
                    FirstName TEXT NULL,
                    LastName TEXT NULL,
                    PhoneNumber TEXT NULL,
                    CompanyId TEXT NOT NULL,
                    Email TEXT NOT NULL,
                    Role TEXT NOT NULL,
                    Status TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );
                """);
        }

        private static string Hash(string value)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(OtpPepper));
            return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(value)));
        }
    }

    private sealed class LoginCodeReadBarrier : DbCommandInterceptor
    {
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int matchingReadCount;

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.Contains("LoginCodes", StringComparison.OrdinalIgnoreCase))
            {
                var readCount = Interlocked.Increment(ref matchingReadCount);
                if (readCount == 2) release.TrySetResult();
                if (readCount <= 2)
                {
                    await release.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
                }
            }

            return result;
        }
    }

    private sealed class UnusedMachineRequestBlobClient : IMachineRequestBlobClient
    {
        public Task EnsurePrivateContainerAsync(CancellationToken cancellationToken = default) =>
            throw new AssertFailedException("OTP atomicity tests must not access Blob storage.");
        public Task UploadAsync(string blobName, Stream content, string contentType, bool overwrite,
            CancellationToken cancellationToken = default) =>
            throw new AssertFailedException("OTP atomicity tests must not access Blob storage.");
        public Task<Stream?> OpenReadAsync(string blobName, CancellationToken cancellationToken = default) =>
            throw new AssertFailedException("OTP atomicity tests must not access Blob storage.");
        public IAsyncEnumerable<string> ListNamesAsync(string? prefix = null,
            CancellationToken cancellationToken = default) =>
            throw new AssertFailedException("OTP atomicity tests must not access Blob storage.");
        public Task DeletePrefixAsync(string prefix, CancellationToken cancellationToken = default) =>
            throw new AssertFailedException("OTP atomicity tests must not access Blob storage.");
    }
}
