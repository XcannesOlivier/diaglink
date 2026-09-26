using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
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
public sealed class OtpSecurityTests
{
    private const string CheckEmailRoute = "/api/auth/check-email";
    private const string RequestCodeRoute = "/api/auth/request-code";
    private const string VerifyCodeRoute = "/api/auth/verify-code";

    [TestMethod]
    public async Task CheckEmail_KnownUser_ReturnsGenericResponse()
    {
        await using var factory = new OtpApplicationFactory();
        await SeedUserAsync(factory.Services, "known@example.test", "active");

        var response = await PostAsync<CheckEmailResponse>(factory, CheckEmailRoute,
            new CheckEmailRequest { Email = "known@example.test" });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsTrue(response.Body.Known);
    }

    [TestMethod]
    public async Task CheckEmail_UnknownUser_ReturnsSameGenericResponse()
    {
        await using var factory = new OtpApplicationFactory();

        var response = await PostAsync<CheckEmailResponse>(factory, CheckEmailRoute,
            new CheckEmailRequest { Email = "unknown@example.test" });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsTrue(response.Body.Known);
    }

    [TestMethod]
    public async Task CheckEmail_InactiveUser_ReturnsSameGenericResponse()
    {
        await using var factory = new OtpApplicationFactory();
        await SeedUserAsync(factory.Services, "inactive@example.test", "inactive");

        var response = await PostAsync<CheckEmailResponse>(factory, CheckEmailRoute,
            new CheckEmailRequest { Email = "inactive@example.test" });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsTrue(response.Body.Known);
    }

    [TestMethod]
    public async Task RequestCode_KnownAndUnknownUsers_ReturnSamePublicResponse()
    {
        await using var factory = new OtpApplicationFactory();
        await SeedUserAsync(factory.Services, "known@example.test", "active");

        var known = await PostAsync<RequestCodeResponse>(factory, RequestCodeRoute,
            new RequestCodeRequest { Email = "known@example.test" });
        var unknown = await PostAsync<RequestCodeResponse>(factory, RequestCodeRoute,
            new RequestCodeRequest { Email = "unknown@example.test" });

        Assert.AreEqual(HttpStatusCode.OK, known.StatusCode);
        Assert.AreEqual(known.StatusCode, unknown.StatusCode);
        Assert.AreEqual(known.Body, unknown.Body);
    }

    [TestMethod]
    public async Task RequestCode_MultipleRequestsBelowEmailLimit_AreAllowed()
    {
        await using var factory = new OtpApplicationFactory();

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var response = await PostRawAsync(factory, RequestCodeRoute,
                new RequestCodeRequest { Email = "normal@example.test" });
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [TestMethod]
    public async Task RequestCode_EmailLimitExceeded_ReturnsTooManyRequests()
    {
        await using var factory = new OtpApplicationFactory();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var response = await PostRawAsync(factory, RequestCodeRoute,
                new RequestCodeRequest { Email = "target@example.test" });
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        }

        using var rejected = await PostRawAsync(factory, RequestCodeRoute,
            new RequestCodeRequest { Email = " TARGET@example.test " });
        Assert.AreEqual(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.IsTrue(rejected.Headers.Contains("Retry-After"));
    }

    [TestMethod]
    public async Task RequestCode_IpLimitExceededAcrossEmails_ReturnsTooManyRequests()
    {
        await using var factory = new OtpApplicationFactory();

        for (var attempt = 0; attempt < 30; attempt++)
        {
            using var response = await PostRawAsync(factory, RequestCodeRoute,
                new RequestCodeRequest { Email = $"target-{attempt}@example.test" });
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        }

        using var rejected = await PostRawAsync(factory, RequestCodeRoute,
            new RequestCodeRequest { Email = "target-final@example.test" });
        Assert.AreEqual(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    }

    [TestMethod]
    public async Task RequestCode_DifferentEmailPartition_RemainsAllowed()
    {
        await using var factory = new OtpApplicationFactory();

        for (var attempt = 0; attempt < 6; attempt++)
        {
            using var response = await PostRawAsync(factory, RequestCodeRoute,
                new RequestCodeRequest { Email = "blocked@example.test" });
            Assert.AreEqual(attempt < 5 ? HttpStatusCode.OK : HttpStatusCode.TooManyRequests, response.StatusCode);
        }

        using var other = await PostRawAsync(factory, RequestCodeRoute,
            new RequestCodeRequest { Email = "other@example.test" });
        Assert.AreEqual(HttpStatusCode.OK, other.StatusCode);
    }

    [TestMethod]
    public async Task VerifyCode_AttemptsBelowEmailLimit_ReturnNormalResponse()
    {
        await using var factory = new OtpApplicationFactory();

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var response = await PostAsync<VerifyCodeResponse>(factory, VerifyCodeRoute,
                new VerifyCodeRequest { Email = "normal@example.test", Code = "000000" });
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsFalse(response.Body.Success);
        }
    }

    [TestMethod]
    public async Task VerifyCode_EmailLimitExceeded_ReturnsTooManyRequests()
    {
        await using var factory = new OtpApplicationFactory();

        for (var attempt = 0; attempt < 10; attempt++)
        {
            using var response = await PostRawAsync(factory, VerifyCodeRoute,
                new VerifyCodeRequest { Email = "target@example.test", Code = "000000" });
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        }

        using var rejected = await PostRawAsync(factory, VerifyCodeRoute,
            new VerifyCodeRequest { Email = "TARGET@example.test", Code = "000000" });
        Assert.AreEqual(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.IsTrue(rejected.Headers.Contains("Retry-After"));
    }

    [TestMethod]
    public async Task VerifyCode_DifferentEmailPartition_RemainsAllowed()
    {
        await using var factory = new OtpApplicationFactory();

        for (var attempt = 0; attempt < 11; attempt++)
        {
            using var response = await PostRawAsync(factory, VerifyCodeRoute,
                new VerifyCodeRequest { Email = "blocked@example.test", Code = "000000" });
            Assert.AreEqual(attempt < 10 ? HttpStatusCode.OK : HttpStatusCode.TooManyRequests, response.StatusCode);
        }

        using var other = await PostRawAsync(factory, VerifyCodeRoute,
            new VerifyCodeRequest { Email = "other@example.test", Code = "000000" });
        Assert.AreEqual(HttpStatusCode.OK, other.StatusCode);
    }

    private static async Task<(HttpStatusCode StatusCode, T Body)> PostAsync<T>(
        OtpApplicationFactory factory, string route, object request)
    {
        using var response = await PostRawAsync(factory, route, request);
        var body = await response.Content.ReadFromJsonAsync<T>();
        Assert.IsNotNull(body);
        return (response.StatusCode, body);
    }

    private static async Task<HttpResponseMessage> PostRawAsync(
        OtpApplicationFactory factory, string route, object request)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        return await client.PostAsJsonAsync(route, request);
    }

    private static async Task SeedUserAsync(IServiceProvider services, string email, string status)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DiagLinkDbContext>();
        var now = DateTime.UtcNow;
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            CompanyId = Guid.NewGuid(),
            Email = email,
            Role = DiagLinkRoles.Technician,
            Status = status,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
    }

    private sealed class OtpApplicationFactory : WebApplicationFactory<BlobStorageService>
    {
        private readonly string databaseName = $"otp-security-{Guid.NewGuid():N}";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Auth:OtpPepper"] = "otp-security-test-pepper",
                    ["AZURE_STORAGE_CONNECTION_STRING"] = string.Empty,
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DiagLinkDbContext>();
                services.RemoveAll<DbContextOptions<DiagLinkDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<DiagLinkDbContext>>();
                services.RemoveAll<BlobStorageService>();
                services.AddDbContext<DiagLinkDbContext>(options => options.UseInMemoryDatabase(databaseName));
                services.AddSingleton<IMachineRequestBlobClient, UnusedMachineRequestBlobClient>();
                services.AddScoped<MachineRequestStorageService>();
            });
        }
    }

    private sealed class UnusedMachineRequestBlobClient : IMachineRequestBlobClient
    {
        public Task EnsurePrivateContainerAsync(CancellationToken cancellationToken = default) =>
            throw new AssertFailedException("OTP security tests must not access Blob storage.");
        public Task UploadAsync(string blobName, Stream content, string contentType, bool overwrite,
            CancellationToken cancellationToken = default) =>
            throw new AssertFailedException("OTP security tests must not access Blob storage.");
        public Task<Stream?> OpenReadAsync(string blobName, CancellationToken cancellationToken = default) =>
            throw new AssertFailedException("OTP security tests must not access Blob storage.");
        public IAsyncEnumerable<string> ListNamesAsync(string? prefix = null,
            CancellationToken cancellationToken = default) =>
            throw new AssertFailedException("OTP security tests must not access Blob storage.");
        public Task DeletePrefixAsync(string prefix, CancellationToken cancellationToken = default) =>
            throw new AssertFailedException("OTP security tests must not access Blob storage.");
    }
}