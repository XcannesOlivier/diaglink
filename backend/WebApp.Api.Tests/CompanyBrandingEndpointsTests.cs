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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

[TestClass]
public sealed class CompanyBrandingEndpointsTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01];

    [TestMethod]
    public async Task Patch_AdminUpdatesAccentForClaimCompany()
    {
        await using var fixture = new Fixture();
        var response = await fixture.PatchAsync(
            DiagLinkRoles.CompanyAdmin,
            fixture.CompanyA,
            new { accentColor = "#a1b2c3" });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("#A1B2C3", fixture.Branding.Read(fixture.CompanyA)!.AccentColor);
        Assert.IsNull(fixture.Branding.Read(fixture.CompanyB));
    }

    [TestMethod]
    public async Task Patch_TechnicianIsForbidden()
    {
        await using var fixture = new Fixture();
        var response = await fixture.PatchAsync(
            DiagLinkRoles.Technician,
            fixture.CompanyA,
            new { accentColor = "#123456" });

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.IsNull(fixture.Branding.Read(fixture.CompanyA));
    }

    [TestMethod]
    public async Task Patch_RejectsClientCompanyIdInQueryOrBody()
    {
        await using var fixture = new Fixture();
        var token = fixture.Token(DiagLinkRoles.CompanyAdmin, fixture.CompanyA);

        using var queryRequest = fixture.Request(
            HttpMethod.Patch,
            $"/api/company/branding?companyId={fixture.CompanyB}",
            token);
        queryRequest.Content = JsonContent.Create(new { accentColor = "#123456" });
        using var queryResponse = await fixture.Client.SendAsync(queryRequest);

        using var bodyRequest = fixture.Request(HttpMethod.Patch, "/api/company/branding", token);
        bodyRequest.Content = JsonContent.Create(new
        {
            accentColor = "#123456",
            companyId = fixture.CompanyB
        });
        using var bodyResponse = await fixture.Client.SendAsync(bodyRequest);

        Assert.AreEqual(HttpStatusCode.BadRequest, queryResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, bodyResponse.StatusCode);
        Assert.IsNull(fixture.Branding.Read(fixture.CompanyA));
        Assert.IsNull(fixture.Branding.Read(fixture.CompanyB));
    }

    [TestMethod]
    public async Task PutLogo_AdminUploadsWithoutExposingBlobName()
    {
        await using var fixture = new Fixture();
        using var response = await fixture.PutLogoAsync(fixture.CompanyA);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        StringAssert.Contains(body, "\"hasLogo\":true");
        Assert.IsFalse(body.Contains("blobName", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(body.Contains("companies/", StringComparison.Ordinal));
        Assert.IsNotNull(fixture.Branding.Read(fixture.CompanyA)?.LogoBlobName);
    }

    [TestMethod]
    public async Task PutLogo_ReplacementPersistsBeforeDeletingOldBlob()
    {
        await using var fixture = new Fixture();
        var oldBlob = fixture.SeedLogo(fixture.CompanyA);
        fixture.Events.Clear();

        using var response = await fixture.PutLogoAsync(fixture.CompanyA);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(3, fixture.Events.Count);
        StringAssert.StartsWith(fixture.Events[0], "upload:");
        StringAssert.StartsWith(fixture.Events[1], "sql-logo:");
        Assert.AreEqual($"delete:{oldBlob}", fixture.Events[2]);
        Assert.IsFalse(fixture.Logos.Contains(oldBlob));
        Assert.AreNotEqual(oldBlob, fixture.Branding.Read(fixture.CompanyA)!.LogoBlobName);
    }

    [TestMethod]
    public async Task PutLogo_SqlFailureAttemptsNewBlobCleanupAndKeepsOldBlob()
    {
        await using var fixture = new Fixture();
        var oldBlob = fixture.SeedLogo(fixture.CompanyA);
        fixture.Events.Clear();
        fixture.Branding.FailNextLogoWrite = true;

        using var response = await fixture.PutLogoAsync(fixture.CompanyA);

        Assert.AreEqual(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.AreEqual(3, fixture.Events.Count);
        StringAssert.StartsWith(fixture.Events[0], "upload:");
        StringAssert.StartsWith(fixture.Events[1], "sql-logo:");
        var uploadedBlob = fixture.Events[0]["upload:".Length..];
        Assert.AreEqual($"delete:{uploadedBlob}", fixture.Events[2]);
        Assert.IsTrue(fixture.Logos.Contains(oldBlob));
        Assert.AreEqual(oldBlob, fixture.Branding.Read(fixture.CompanyA)!.LogoBlobName);
    }

    [TestMethod]
    public async Task DeleteLogo_ClearsSqlBeforeDeletingBlob()
    {
        await using var fixture = new Fixture();
        var oldBlob = fixture.SeedLogo(fixture.CompanyA, "#123456");
        fixture.Events.Clear();

        using var response = await fixture.SendAsync(
            HttpMethod.Delete,
            "/api/company/branding/logo",
            DiagLinkRoles.CompanyAdmin,
            fixture.CompanyA);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        CollectionAssert.AreEqual(
            new[] { "sql-clear", $"delete:{oldBlob}" },
            fixture.Events);
        Assert.IsNull(fixture.Branding.Read(fixture.CompanyA)!.LogoBlobName);
    }

    [TestMethod]
    public async Task DeleteLogo_MissingBlobIsTolerated()
    {
        await using var fixture = new Fixture();
        var missingBlob = fixture.SeedLogo(fixture.CompanyA, addBlob: false);

        using var response = await fixture.SendAsync(
            HttpMethod.Delete,
            "/api/company/branding/logo",
            DiagLinkRoles.CompanyAdmin,
            fixture.CompanyA);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsFalse(fixture.Logos.Contains(missingBlob));
        Assert.IsNull(fixture.Branding.Read(fixture.CompanyA));
    }

    [TestMethod]
    public async Task DeleteBranding_ResetsSqlAndDeletesLogo()
    {
        await using var fixture = new Fixture();
        var oldBlob = fixture.SeedLogo(fixture.CompanyA, "#123456");
        fixture.Events.Clear();

        using var response = await fixture.SendAsync(
            HttpMethod.Delete,
            "/api/company/branding",
            DiagLinkRoles.CompanyAdmin,
            fixture.CompanyA);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        CollectionAssert.AreEqual(
            new[] { "sql-reset", $"delete:{oldBlob}" },
            fixture.Events);
        Assert.IsNull(fixture.Branding.Read(fixture.CompanyA));
        Assert.IsFalse(fixture.Logos.Contains(oldBlob));
    }

    [TestMethod]
    public async Task GetLogo_TechnicianReadsOwnCompanyLogo()
    {
        await using var fixture = new Fixture();
        fixture.SeedLogo(fixture.CompanyA);

        using var response = await fixture.SendAsync(
            HttpMethod.Get,
            "/api/company/branding/logo",
            DiagLinkRoles.Technician,
            fixture.CompanyA);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("image/png", response.Content.Headers.ContentType?.MediaType);
        CollectionAssert.AreEqual(Png, await response.Content.ReadAsByteArrayAsync());
    }

    [TestMethod]
    public async Task GetLogo_OtherCompanyCannotReadLogo()
    {
        await using var fixture = new Fixture();
        fixture.SeedLogo(fixture.CompanyA);

        using var response = await fixture.SendAsync(
            HttpMethod.Get,
            "/api/company/branding/logo",
            DiagLinkRoles.Technician,
            fixture.CompanyB);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly BrandingApplicationFactory factory = new();

        public Fixture()
        {
            Client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        }

        public Guid CompanyA { get; } = Guid.NewGuid();
        public Guid CompanyB { get; } = Guid.NewGuid();
        public HttpClient Client { get; }
        public FakeBrandingService Branding => factory.Branding;
        public FakeLogoStorage Logos => factory.Logos;
        public List<string> Events => factory.Events;

        public string Token(string role, Guid companyId) => factory.Identities.Add(role, companyId);

        public HttpRequestMessage Request(HttpMethod method, string path, string token)
        {
            var request = new HttpRequestMessage(method, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return request;
        }

        public async Task<HttpResponseMessage> PatchAsync(string role, Guid companyId, object body)
        {
            var request = Request(HttpMethod.Patch, "/api/company/branding", Token(role, companyId));
            request.Content = JsonContent.Create(body);
            return await Client.SendAsync(request);
        }

        public async Task<HttpResponseMessage> PutLogoAsync(Guid companyId)
        {
            var request = Request(
                HttpMethod.Put,
                "/api/company/branding/logo",
                Token(DiagLinkRoles.CompanyAdmin, companyId));
            var multipart = new MultipartFormDataContent();
            var logo = new ByteArrayContent(Png);
            logo.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            multipart.Add(logo, "logo", "company-logo.png");
            request.Content = multipart;
            return await Client.SendAsync(request);
        }

        public async Task<HttpResponseMessage> SendAsync(
            HttpMethod method,
            string path,
            string role,
            Guid companyId)
        {
            using var request = Request(method, path, Token(role, companyId));
            return await Client.SendAsync(request);
        }

        public string SeedLogo(Guid companyId, string? accentColor = null, bool addBlob = true)
        {
            var blobName = $"companies/{companyId:N}/logos/{Guid.NewGuid():N}.png";
            Branding.Seed(new CompanyBranding
            {
                CompanyId = companyId,
                AccentColor = accentColor,
                LogoBlobName = blobName,
                LogoContentType = "image/png",
                UpdatedAtUtc = DateTime.UtcNow
            });
            if (addBlob)
            {
                Logos.Seed(blobName, Png, "image/png");
            }
            return blobName;
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await factory.DisposeAsync();
        }
    }

    private sealed class BrandingApplicationFactory : WebApplicationFactory<BlobStorageService>
    {
        public List<string> Events { get; } = [];
        public TestIdentityStore Identities { get; } = new();
        public FakeBrandingService Branding { get; }
        public FakeLogoStorage Logos { get; }

        public BrandingApplicationFactory()
        {
            Branding = new FakeBrandingService(Events);
            Logos = new FakeLogoStorage(Events);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DiagLink"] = "Server=(local);Database=company-branding-tests",
                    ["AZURE_STORAGE_CONNECTION_STRING"] = string.Empty,
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveWindowsEventLogProvider();
                services.RemoveAll<ICompanyBrandingService>();
                services.RemoveAll<ICompanyLogoStorageService>();
                services.AddSingleton<ICompanyBrandingService>(Branding);
                services.AddSingleton<ICompanyLogoStorageService>(Logos);
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

    private sealed class UnusedMachineRequestBlobClient : IMachineRequestBlobClient
    {
        private const string Message = "The company branding endpoint tests must not access machine request Blob storage.";

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

    private sealed record TestIdentity(string Role, Guid CompanyId, Guid UserId);

    private sealed class TestIdentityStore
    {
        private readonly ConcurrentDictionary<string, TestIdentity> identities = new();

        public string Add(string role, Guid companyId)
        {
            var token = Guid.NewGuid().ToString("N");
            identities[token] = new TestIdentity(role, companyId, Guid.NewGuid());
            return token;
        }

        public bool TryGet(string token, out TestIdentity? identity) =>
            identities.TryGetValue(token, out identity);
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
                new(ClaimTypes.Role, identity.Role),
            ];
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                claims,
                Scheme.Name,
                ClaimTypes.Name,
                ClaimTypes.Role));
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(principal, Scheme.Name)));
        }
    }

    private sealed class FakeBrandingService(List<string> events) : ICompanyBrandingService
    {
        private readonly ConcurrentDictionary<Guid, CompanyBranding> brandings = new();

        public bool FailNextLogoWrite { get; set; }

        public CompanyBranding? Read(Guid companyId) =>
            brandings.TryGetValue(companyId, out var branding) ? Clone(branding) : null;

        public void Seed(CompanyBranding branding) => brandings[branding.CompanyId] = Clone(branding);

        public Task<CompanyBranding?> GetAsync(Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Read(companyId));

        public Task<CompanyBranding?> SetAccentColorAsync(
            Guid companyId,
            string? accentColor,
            CancellationToken cancellationToken = default)
        {
            events.Add("sql-accent");
            var current = Read(companyId) ?? new CompanyBranding { CompanyId = companyId };
            current.AccentColor = accentColor?.ToUpperInvariant();
            current.UpdatedAtUtc = DateTime.UtcNow;
            if (current.AccentColor is null && current.LogoBlobName is null)
            {
                brandings.TryRemove(companyId, out _);
                return Task.FromResult<CompanyBranding?>(null);
            }
            brandings[companyId] = Clone(current);
            return Task.FromResult<CompanyBranding?>(Clone(current));
        }

        public Task<CompanyBranding> SetLogoMetadataAsync(
            Guid companyId,
            string logoBlobName,
            string logoContentType,
            CancellationToken cancellationToken = default)
        {
            events.Add($"sql-logo:{logoBlobName}");
            if (FailNextLogoWrite)
            {
                FailNextLogoWrite = false;
                throw new InvalidOperationException("Simulated SQL failure.");
            }
            var current = Read(companyId) ?? new CompanyBranding { CompanyId = companyId };
            current.LogoBlobName = logoBlobName;
            current.LogoContentType = logoContentType;
            current.UpdatedAtUtc = DateTime.UtcNow;
            brandings[companyId] = Clone(current);
            return Task.FromResult(Clone(current));
        }

        public Task<CompanyBranding?> ClearLogoMetadataAsync(
            Guid companyId,
            CancellationToken cancellationToken = default)
        {
            events.Add("sql-clear");
            var current = Read(companyId);
            if (current is null)
            {
                return Task.FromResult<CompanyBranding?>(null);
            }
            current.LogoBlobName = null;
            current.LogoContentType = null;
            current.UpdatedAtUtc = DateTime.UtcNow;
            if (current.AccentColor is null)
            {
                brandings.TryRemove(companyId, out _);
                return Task.FromResult<CompanyBranding?>(null);
            }
            brandings[companyId] = Clone(current);
            return Task.FromResult<CompanyBranding?>(Clone(current));
        }

        public Task ResetAsync(Guid companyId, CancellationToken cancellationToken = default)
        {
            events.Add("sql-reset");
            brandings.TryRemove(companyId, out _);
            return Task.CompletedTask;
        }

        private static CompanyBranding Clone(CompanyBranding branding) => new()
        {
            CompanyId = branding.CompanyId,
            AccentColor = branding.AccentColor,
            LogoBlobName = branding.LogoBlobName,
            LogoContentType = branding.LogoContentType,
            UpdatedAtUtc = branding.UpdatedAtUtc,
        };
    }

    private sealed class FakeLogoStorage(List<string> events) : ICompanyLogoStorageService
    {
        private readonly ConcurrentDictionary<string, StoredLogo> logos = new();

        public bool Contains(string blobName) => logos.ContainsKey(blobName);

        public void Seed(string blobName, byte[] content, string contentType) =>
            logos[blobName] = new StoredLogo(content.ToArray(), contentType, "\"seed\"");

        public async Task<CompanyLogoMetadata> UploadAsync(
            Guid companyId,
            Stream content,
            string? originalFileName,
            string? declaredContentType,
            CancellationToken cancellationToken = default)
        {
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, cancellationToken);
            var blobName = $"companies/{companyId:N}/logos/{Guid.NewGuid():N}.png";
            events.Add($"upload:{blobName}");
            logos[blobName] = new StoredLogo(copy.ToArray(), "image/png", "\"uploaded\"");
            return new CompanyLogoMetadata(blobName, "image/png", "\"uploaded\"");
        }

        public Task<CompanyLogoContent?> OpenReadAsync(
            Guid companyId,
            string blobName,
            CancellationToken cancellationToken = default)
        {
            if (!blobName.StartsWith($"companies/{companyId:N}/logos/", StringComparison.Ordinal) ||
                !logos.TryGetValue(blobName, out var logo))
            {
                return Task.FromResult<CompanyLogoContent?>(null);
            }
            return Task.FromResult<CompanyLogoContent?>(new(
                new MemoryStream(logo.Content, writable: false),
                logo.ContentType,
                logo.ETag));
        }

        public Task<bool> DeleteAsync(
            Guid companyId,
            string blobName,
            CancellationToken cancellationToken = default)
        {
            events.Add($"delete:{blobName}");
            return Task.FromResult(logos.TryRemove(blobName, out _));
        }
    }

    private sealed record StoredLogo(byte[] Content, string ContentType, string ETag);
}
