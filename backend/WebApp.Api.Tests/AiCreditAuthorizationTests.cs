using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Data;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

[TestClass]
public sealed class AiCreditAuthorizationTests
{
    private const string OtpPepper = "ai-credit-authorization-test-pepper";

    [TestMethod]
    public async Task Get_Anonymous_ReturnsUnauthorized()
    {
        await using var factory = new AiCreditApplicationFactory();
        var fixture = await factory.SeedAsync();

        Assert.AreEqual(HttpStatusCode.Unauthorized,
            await GetAsync(factory, fixture.MachineA));
    }

    [TestMethod]
    public async Task Get_JwtTechnicianWithMachineAccess_ReturnsOk()
    {
        await using var factory = new AiCreditApplicationFactory();
        var fixture = await factory.SeedAsync();
        var userId = Guid.NewGuid();
        await factory.GrantMachineAsync(userId, fixture.MachineA);
        var token = factory.AddJwt(DiagLinkRoles.Technician, fixture.CompanyA, userId);

        Assert.AreEqual(HttpStatusCode.OK,
            await GetAsync(factory, fixture.MachineA, bearerToken: token));
    }

    [TestMethod]
    public async Task Get_OtpTechnicianWithMachineAccess_ReturnsOk()
    {
        await using var factory = new AiCreditApplicationFactory();
        var fixture = await factory.SeedAsync();
        var session = await factory.AddOtpSessionAsync(
            DiagLinkRoles.Technician, fixture.CompanyA, fixture.MachineA);

        Assert.AreEqual(HttpStatusCode.OK,
            await GetAsync(factory, fixture.MachineA, sessionToken: session));
    }

    [TestMethod]
    public async Task Get_OtpTechnicianWithoutMachineAccess_ReturnsNotFound()
    {
        await using var factory = new AiCreditApplicationFactory();
        var fixture = await factory.SeedAsync();
        var session = await factory.AddOtpSessionAsync(DiagLinkRoles.Technician, fixture.CompanyA);

        Assert.AreEqual(HttpStatusCode.NotFound,
            await GetAsync(factory, fixture.MachineA, sessionToken: session));
    }

    [TestMethod]
    public async Task Get_OtpCompanyAdminFromAnotherCompany_ReturnsNotFound()
    {
        await using var factory = new AiCreditApplicationFactory();
        var fixture = await factory.SeedAsync();
        var session = await factory.AddOtpSessionAsync(DiagLinkRoles.CompanyAdmin, fixture.CompanyB);

        Assert.AreEqual(HttpStatusCode.NotFound,
            await GetAsync(factory, fixture.MachineA, sessionToken: session));
    }

    [TestMethod]
    public async Task Get_OtpUserWithInsufficientRole_ReturnsForbidden()
    {
        await using var factory = new AiCreditApplicationFactory();
        var fixture = await factory.SeedAsync();
        var session = await factory.AddOtpSessionAsync("viewer", fixture.CompanyA);

        Assert.AreEqual(HttpStatusCode.Forbidden,
            await GetAsync(factory, fixture.MachineA, sessionToken: session));
    }

    [TestMethod]
    public async Task Get_JwtSuperAdmin_ReturnsOk()
    {
        await using var factory = new AiCreditApplicationFactory();
        var fixture = await factory.SeedAsync();
        var token = factory.AddJwt(DiagLinkRoles.SuperAdmin, Guid.NewGuid(), Guid.NewGuid());

        Assert.AreEqual(HttpStatusCode.OK,
            await GetAsync(factory, fixture.MachineA, bearerToken: token));
    }

    private static async Task<HttpStatusCode> GetAsync(AiCreditApplicationFactory factory, Guid machineId,
        string? bearerToken = null, string? sessionToken = null)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/machines/{machineId}/ai-credit");
        if (bearerToken is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        if (sessionToken is not null)
            request.Headers.Add(DiagLinkAuthenticationDefaults.HeaderName, sessionToken);
        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    private sealed record TestFixture(Guid CompanyA, Guid CompanyB, Guid MachineA);

    private sealed class AiCreditApplicationFactory : WebApplicationFactory<BlobStorageService>
    {
        private readonly string databaseName = $"ai-credit-auth-{Guid.NewGuid():N}";
        private readonly TestJwtIdentityStore jwtIdentities = new();

        public async Task<TestFixture> SeedAsync()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiagLinkDbContext>();
            var now = DateTime.UtcNow;
            var companyA = Guid.NewGuid();
            var companyB = Guid.NewGuid();
            var machineA = Guid.NewGuid();
            db.Companies.AddRange(
                new Company { Id = companyA, Name = "Company A", Status = "active", CreatedAtUtc = now, UpdatedAtUtc = now },
                new Company { Id = companyB, Name = "Company B", Status = "active", CreatedAtUtc = now, UpdatedAtUtc = now });
            db.Machines.Add(new Machine
            {
                Id = machineA,
                CompanyId = companyA,
                Name = "Press A",
                Status = "active",
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            });
            db.CompanyWallets.Add(new CompanyWallet
            {
                CompanyId = companyA,
                Balance = 10m,
                Currency = "EUR",
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            });
            await db.SaveChangesAsync();
            return new TestFixture(companyA, companyB, machineA);
        }

        public string AddJwt(string role, Guid companyId, Guid userId) =>
            jwtIdentities.Add(role, companyId, userId);

        public async Task<string> AddOtpSessionAsync(string role, Guid companyId, Guid? machineId = null)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiagLinkDbContext>();
            var now = DateTime.UtcNow;
            var userId = Guid.NewGuid();
            var token = $"test-session-{Guid.NewGuid():N}";
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(OtpPepper));
            var tokenHash = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(token)));
            db.Users.Add(new User
            {
                Id = userId,
                CompanyId = companyId,
                Email = $"{userId:N}@test.local",
                Role = role,
                Status = "active",
                CreatedAt = now,
                UpdatedAt = now,
            });
            db.UserSessions.Add(new UserSession
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TokenHash = tokenHash,
                CreatedAtUtc = now,
                ExpiresAtUtc = now.AddHours(1),
            });
            if (machineId is not null)
                db.UserMachineAccess.Add(new UserMachineAccess
                    { UserId = userId, MachineId = machineId.Value, CreatedAtUtc = now });
            await db.SaveChangesAsync();
            return token;
        }

        public async Task GrantMachineAsync(Guid userId, Guid machineId)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiagLinkDbContext>();
            db.UserMachineAccess.Add(new UserMachineAccess
                { UserId = userId, MachineId = machineId, CreatedAtUtc = DateTime.UtcNow });
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
                services.RemoveWindowsEventLogProvider();
                services.RemoveAll<DiagLinkDbContext>();
                services.RemoveAll<DbContextOptions<DiagLinkDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<DiagLinkDbContext>>();
                services.AddDbContext<DiagLinkDbContext>(options => options.UseInMemoryDatabase(databaseName));
                services.AddSingleton<IMachineRequestBlobClient, UnusedMachineRequestBlobClient>();
                services.AddScoped<MachineRequestStorageService>();

                services.RemoveAll<IConfigureOptions<AuthenticationOptions>>();
                services.AddSingleton(jwtIdentities);
                services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestJwtAuthenticationHandler>(
                        JwtBearerDefaults.AuthenticationScheme, _ => { })
                    .AddScheme<AuthenticationSchemeOptions, DiagLinkSessionAuthenticationHandler>(
                        DiagLinkAuthenticationDefaults.Scheme, _ => { });
            });
        }
    }

    private sealed record TestJwtIdentity(string Role, Guid CompanyId, Guid UserId);

    private sealed class TestJwtIdentityStore
    {
        private readonly ConcurrentDictionary<string, TestJwtIdentity> identities = new();

        public string Add(string role, Guid companyId, Guid userId)
        {
            var token = Guid.NewGuid().ToString("N");
            identities[token] = new TestJwtIdentity(role, companyId, userId);
            return token;
        }

        public bool TryGet(string token, out TestJwtIdentity? identity) => identities.TryGetValue(token, out identity);
    }

    private sealed class TestJwtAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        TestJwtIdentityStore identities)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var authorization = Request.Headers.Authorization.ToString();
            if (!authorization.StartsWith("Bearer ", StringComparison.Ordinal))
                return Task.FromResult(AuthenticateResult.NoResult());
            if (!identities.TryGet(authorization["Bearer ".Length..], out var identity) || identity is null)
                return Task.FromResult(AuthenticateResult.Fail("Invalid test JWT."));
            Claim[] claims =
            [
                new(ClaimTypes.NameIdentifier, identity.UserId.ToString()),
                new(DiagLinkClaimTypes.UserId, identity.UserId.ToString()),
                new(ClaimTypes.Role, identity.Role),
                new(DiagLinkClaimTypes.CompanyId, identity.CompanyId.ToString()),
            ];
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                claims, JwtBearerDefaults.AuthenticationScheme, ClaimTypes.Name, ClaimTypes.Role));
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(principal, JwtBearerDefaults.AuthenticationScheme)));
        }
    }

    private sealed class UnusedMachineRequestBlobClient : IMachineRequestBlobClient
    {
        public Task EnsurePrivateContainerAsync(CancellationToken cancellationToken = default) =>
            throw new AssertFailedException("The AI credit authorization tests must not access Blob storage.");

        public Task UploadAsync(string blobName, Stream content, string contentType, bool overwrite,
            CancellationToken cancellationToken = default) =>
            throw new AssertFailedException("The AI credit authorization tests must not access Blob storage.");

        public Task<Stream?> OpenReadAsync(string blobName, CancellationToken cancellationToken = default) =>
            throw new AssertFailedException("The AI credit authorization tests must not access Blob storage.");

        public IAsyncEnumerable<string> ListNamesAsync(string? prefix = null,
            CancellationToken cancellationToken = default) =>
            throw new AssertFailedException("The AI credit authorization tests must not access Blob storage.");

        public Task DeletePrefixAsync(string prefix, CancellationToken cancellationToken = default) =>
            throw new AssertFailedException("The AI credit authorization tests must not access Blob storage.");
    }
}
