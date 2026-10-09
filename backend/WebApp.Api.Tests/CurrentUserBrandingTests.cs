using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
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
public sealed class CurrentUserBrandingTests
{
    private static readonly DateTime LogoUpdatedAtUtc = new(2026, 10, 9, 12, 34, 56, DateTimeKind.Utc);

    [TestMethod]
    public async Task GetMe_CompanyAdminReturnsCompleteCompanyBranding()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedBrandingAsync(fixture.CompanyA, "#123456", withLogo: true);

        using var response = await fixture.GetMeAsync(fixture.AdminUserId, fixture.CompanyA, DiagLinkRoles.CompanyAdmin);
        var currentUser = await ReadAsync(response);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsNotNull(currentUser.CompanyBranding);
        Assert.AreEqual("Entreprise A", currentUser.CompanyBranding.CompanyName);
        Assert.AreEqual("#123456", currentUser.CompanyBranding.AccentColor);
        Assert.IsTrue(currentUser.CompanyBranding.HasLogo);
        Assert.AreEqual(LogoUpdatedAtUtc.Ticks.ToString(), currentUser.CompanyBranding.LogoVersion);
    }

    [TestMethod]
    public async Task GetMe_TechnicianReturnsCompleteCompanyBranding()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedBrandingAsync(fixture.CompanyA, "#ABCDEF", withLogo: true);

        using var response = await fixture.GetMeAsync(fixture.TechnicianUserId, fixture.CompanyA, DiagLinkRoles.Technician);
        var currentUser = await ReadAsync(response);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("Entreprise A", currentUser.CompanyBranding?.CompanyName);
        Assert.AreEqual("#ABCDEF", currentUser.CompanyBranding?.AccentColor);
        Assert.IsTrue(currentUser.CompanyBranding?.HasLogo);
    }

    [TestMethod]
    public async Task GetMe_CompanyWithoutBrandingReturnsDefaultsAndCompanyName()
    {
        await using var fixture = await Fixture.CreateAsync();

        using var response = await fixture.GetMeAsync(fixture.AdminUserId, fixture.CompanyA, DiagLinkRoles.CompanyAdmin);
        var branding = (await ReadAsync(response)).CompanyBranding;

        Assert.IsNotNull(branding);
        Assert.AreEqual("Entreprise A", branding.CompanyName);
        Assert.IsNull(branding.AccentColor);
        Assert.IsFalse(branding.HasLogo);
        Assert.IsNull(branding.LogoVersion);
    }

    [TestMethod]
    public async Task GetMe_NullAccentRemainsNull()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedBrandingAsync(fixture.CompanyA, accentColor: null, withLogo: true);

        using var response = await fixture.GetMeAsync(fixture.AdminUserId, fixture.CompanyA, DiagLinkRoles.CompanyAdmin);
        var branding = (await ReadAsync(response)).CompanyBranding;

        Assert.IsNotNull(branding);
        Assert.IsNull(branding.AccentColor);
        Assert.IsTrue(branding.HasLogo);
    }

    [TestMethod]
    public async Task GetMe_AbsentLogoReturnsFalseWithoutVersion()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedBrandingAsync(fixture.CompanyA, "#123456", withLogo: false);

        using var response = await fixture.GetMeAsync(fixture.AdminUserId, fixture.CompanyA, DiagLinkRoles.CompanyAdmin);
        var branding = (await ReadAsync(response)).CompanyBranding;

        Assert.IsNotNull(branding);
        Assert.IsFalse(branding.HasLogo);
        Assert.IsNull(branding.LogoVersion);
    }

    [TestMethod]
    public async Task GetMe_ConfiguredLogoReturnsStableVersion()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedBrandingAsync(fixture.CompanyA, "#123456", withLogo: true);

        using var firstResponse = await fixture.GetMeAsync(fixture.AdminUserId, fixture.CompanyA, DiagLinkRoles.CompanyAdmin);
        using var secondResponse = await fixture.GetMeAsync(fixture.AdminUserId, fixture.CompanyA, DiagLinkRoles.CompanyAdmin);
        var firstVersion = (await ReadAsync(firstResponse)).CompanyBranding?.LogoVersion;
        var secondVersion = (await ReadAsync(secondResponse)).CompanyBranding?.LogoVersion;

        Assert.AreEqual(LogoUpdatedAtUtc.Ticks.ToString(), firstVersion);
        Assert.AreEqual(firstVersion, secondVersion);
    }

    [TestMethod]
    public async Task GetMe_SuperAdminReturnsNoCompanyBranding()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedBrandingAsync(fixture.CompanyA, "#123456", withLogo: true);

        using var response = await fixture.GetMeAsync(fixture.SuperAdminUserId, fixture.CompanyA, DiagLinkRoles.SuperAdmin);
        var currentUser = await ReadAsync(response);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsNull(currentUser.CompanyBranding);
    }

    [TestMethod]
    public async Task GetMe_NeverExposesLogoBlobName()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedBrandingAsync(fixture.CompanyA, "#123456", withLogo: true);

        using var response = await fixture.GetMeAsync(fixture.AdminUserId, fixture.CompanyA, DiagLinkRoles.CompanyAdmin);
        var json = await response.Content.ReadAsStringAsync();

        Assert.IsFalse(json.Contains("logoBlobName", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains($"companies/{fixture.CompanyA:N}/", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task GetMe_UsesOnlyCallerTenantBranding()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedBrandingAsync(fixture.CompanyA, "#AABBCC", withLogo: true);

        using var response = await fixture.GetMeAsync(fixture.OtherTechnicianUserId, fixture.CompanyB, DiagLinkRoles.Technician);
        var branding = (await ReadAsync(response)).CompanyBranding;

        Assert.IsNotNull(branding);
        Assert.AreEqual("Entreprise B", branding.CompanyName);
        Assert.IsNull(branding.AccentColor);
        Assert.IsFalse(branding.HasLogo);
        Assert.IsNull(branding.LogoVersion);
    }

    [TestMethod]
    public async Task GetMe_PreservesExistingUserFields()
    {
        await using var fixture = await Fixture.CreateAsync();

        using var response = await fixture.GetMeAsync(fixture.AdminUserId, fixture.CompanyA, DiagLinkRoles.CompanyAdmin);
        var currentUser = await ReadAsync(response);

        Assert.AreEqual(fixture.AdminUserId.ToString(), currentUser.UserId);
        Assert.AreEqual(fixture.CompanyA.ToString(), currentUser.CompanyId);
        Assert.AreEqual(DiagLinkRoles.CompanyAdmin, currentUser.Role);
        Assert.AreEqual("admin@example.test", currentUser.Email);
        Assert.AreEqual("Ada", currentUser.FirstName);
        Assert.AreEqual("Lovelace", currentUser.LastName);
        Assert.AreEqual("+33123456789", currentUser.PhoneNumber);
    }

    private static async Task<CurrentUserResponse> ReadAsync(HttpResponseMessage response)
    {
        var currentUser = await response.Content.ReadFromJsonAsync<CurrentUserResponse>();
        Assert.IsNotNull(currentUser);
        return currentUser;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly CurrentUserApplicationFactory factory = new();

        private Fixture()
        {
            Client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        }

        public HttpClient Client { get; }
        public Guid CompanyA { get; } = Guid.NewGuid();
        public Guid CompanyB { get; } = Guid.NewGuid();
        public Guid AdminUserId { get; } = Guid.NewGuid();
        public Guid TechnicianUserId { get; } = Guid.NewGuid();
        public Guid OtherTechnicianUserId { get; } = Guid.NewGuid();
        public Guid SuperAdminUserId { get; } = Guid.NewGuid();

        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            await fixture.SeedBaseDataAsync();
            return fixture;
        }

        public async Task<HttpResponseMessage> GetMeAsync(Guid userId, Guid companyId, string role)
        {
            var token = factory.Identities.Add(userId, companyId, role);
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return await Client.SendAsync(request);
        }

        public async Task SeedBrandingAsync(Guid companyId, string? accentColor, bool withLogo)
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiagLinkDbContext>();
            db.CompanyBrandings.Add(new CompanyBranding
            {
                CompanyId = companyId,
                AccentColor = accentColor,
                LogoBlobName = withLogo ? $"companies/{companyId:N}/logos/private.png" : null,
                LogoContentType = withLogo ? "image/png" : null,
                UpdatedAtUtc = LogoUpdatedAtUtc
            });
            await db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await factory.DisposeAsync();
        }

        private async Task SeedBaseDataAsync()
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiagLinkDbContext>();
            var now = DateTime.UtcNow;
            db.Companies.AddRange(
                new Company { Id = CompanyA, Name = "Entreprise A", Status = "active", CreatedAtUtc = now, UpdatedAtUtc = now },
                new Company { Id = CompanyB, Name = "Entreprise B", Status = "active", CreatedAtUtc = now, UpdatedAtUtc = now });
            db.Users.AddRange(
                User(AdminUserId, CompanyA, DiagLinkRoles.CompanyAdmin, "admin@example.test", "Ada", "Lovelace", "+33123456789", now),
                User(TechnicianUserId, CompanyA, DiagLinkRoles.Technician, "tech@example.test", "Grace", "Hopper", null, now),
                User(OtherTechnicianUserId, CompanyB, DiagLinkRoles.Technician, "other@example.test", "Katherine", "Johnson", null, now),
                User(SuperAdminUserId, CompanyA, DiagLinkRoles.SuperAdmin, "super@example.test", "Margaret", "Hamilton", null, now));
            await db.SaveChangesAsync();
        }

        private static User User(
            Guid id,
            Guid companyId,
            string role,
            string email,
            string firstName,
            string lastName,
            string? phoneNumber,
            DateTime now) => new()
        {
            Id = id,
            CompanyId = companyId,
            Email = email,
            Role = role,
            Status = "active",
            FirstName = firstName,
            LastName = lastName,
            PhoneNumber = phoneNumber,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private sealed class CurrentUserApplicationFactory : WebApplicationFactory<BlobStorageService>
    {
        private readonly string databaseName = $"current-user-branding-{Guid.NewGuid():N}";

        public TestIdentityStore Identities { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DiagLink"] = "Server=(local);Database=current-user-branding-tests",
                    ["AZURE_STORAGE_CONNECTION_STRING"] = string.Empty
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
                services.AddSingleton(Identities);
                services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                        JwtBearerDefaults.AuthenticationScheme,
                        _ => { })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                        DiagLinkAuthenticationDefaults.Scheme,
                        _ => { });
            });
        }
    }

    private sealed record TestIdentity(Guid UserId, Guid CompanyId, string Role);

    private sealed class TestIdentityStore
    {
        private readonly ConcurrentDictionary<string, TestIdentity> identities = new();

        public string Add(Guid userId, Guid companyId, string role)
        {
            var token = Guid.NewGuid().ToString("N");
            identities[token] = new TestIdentity(userId, companyId, role);
            return token;
        }

        public bool TryGet(string token, out TestIdentity? identity) => identities.TryGetValue(token, out identity);
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        TestIdentityStore identities)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!string.Equals(Scheme.Name, JwtBearerDefaults.AuthenticationScheme, StringComparison.Ordinal))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var authorization = Request.Headers.Authorization.ToString();
            if (!authorization.StartsWith("Bearer ", StringComparison.Ordinal) ||
                !identities.TryGet(authorization["Bearer ".Length..], out var identity) ||
                identity is null)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            Claim[] claims =
            [
                new(DiagLinkClaimTypes.UserId, identity.UserId.ToString()),
                new(DiagLinkClaimTypes.CompanyId, identity.CompanyId.ToString()),
                new(ClaimTypes.Role, identity.Role)
            ];
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                claims,
                Scheme.Name,
                ClaimTypes.Name,
                ClaimTypes.Role));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }

    private sealed class UnusedMachineRequestBlobClient : IMachineRequestBlobClient
    {
        private const string Message = "The current-user branding tests must not access Blob storage.";

        public Task EnsurePrivateContainerAsync(CancellationToken cancellationToken = default) =>
            throw new AssertFailedException(Message);

        public Task UploadAsync(
            string blobName,
            Stream content,
            string contentType,
            bool overwrite,
            CancellationToken cancellationToken = default) =>
            throw new AssertFailedException(Message);

        public Task<Stream?> OpenReadAsync(string blobName, CancellationToken cancellationToken = default) =>
            throw new AssertFailedException(Message);

        public IAsyncEnumerable<string> ListNamesAsync(
            string? prefix = null,
            CancellationToken cancellationToken = default) =>
            throw new AssertFailedException(Message);

        public Task DeletePrefixAsync(string prefix, CancellationToken cancellationToken = default) =>
            throw new AssertFailedException(Message);
    }
}
