using System.Net;
using System.Security.Cryptography;
using System.Text;
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
public sealed class LegacyFileUploadAuthorizationTests
{
    private const string Route = "/api/files/upload";
    private const string OtpPepper = "legacy-upload-authorization-test-pepper";

    [TestMethod]
    public async Task Upload_Anonymous_ReturnsUnauthorized()
        => Assert.AreEqual(HttpStatusCode.Unauthorized, await PostAsync());

    [TestMethod]
    public async Task Upload_Technician_ReturnsForbidden()
        => Assert.AreEqual(HttpStatusCode.Forbidden, await PostAsync(DiagLinkRoles.Technician));

    [TestMethod]
    public async Task Upload_CompanyAdmin_ReturnsForbidden()
        => Assert.AreEqual(HttpStatusCode.Forbidden, await PostAsync(DiagLinkRoles.CompanyAdmin));

    [TestMethod]
    public async Task Upload_SuperAdmin_ReachesEndpointWithoutAzureBlob()
        => Assert.AreEqual(HttpStatusCode.NotImplemented, await PostAsync(DiagLinkRoles.SuperAdmin));

    private static async Task<HttpStatusCode> PostAsync(string? role = null)
    {
        await using var factory = new UploadApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, Route);

        if (role is not null)
        {
            var token = $"test-session-{Guid.NewGuid():N}";
            await SeedSessionAsync(factory.Services, token, role);
            request.Headers.Add(DiagLinkAuthenticationDefaults.HeaderName, token);
        }

        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    private static async Task SeedSessionAsync(IServiceProvider services, string token, string role)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DiagLinkDbContext>();
        var now = DateTime.UtcNow;
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(OtpPepper));
        var tokenHash = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(token)));

        db.Users.Add(new User
        {
            Id = userId,
            CompanyId = companyId,
            Email = $"{role}-{userId:N}@test.local",
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
        await db.SaveChangesAsync();
    }

    private sealed class UploadApplicationFactory : WebApplicationFactory<BlobStorageService>
    {
        private readonly string databaseName = $"legacy-upload-auth-{Guid.NewGuid():N}";

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
            throw new AssertFailedException("The legacy upload authorization tests must not access Blob storage.");

        public Task UploadAsync(string blobName, Stream content, string contentType, bool overwrite,
            CancellationToken cancellationToken = default) =>
            throw new AssertFailedException("The legacy upload authorization tests must not access Blob storage.");

        public Task<Stream?> OpenReadAsync(string blobName, CancellationToken cancellationToken = default) =>
            throw new AssertFailedException("The legacy upload authorization tests must not access Blob storage.");

        public IAsyncEnumerable<string> ListNamesAsync(string? prefix = null,
            CancellationToken cancellationToken = default) =>
            throw new AssertFailedException("The legacy upload authorization tests must not access Blob storage.");

        public Task DeletePrefixAsync(string prefix, CancellationToken cancellationToken = default) =>
            throw new AssertFailedException("The legacy upload authorization tests must not access Blob storage.");
    }
}
