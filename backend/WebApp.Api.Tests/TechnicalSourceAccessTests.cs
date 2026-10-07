using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Data;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

[TestClass]
public sealed class TechnicalSourceAccessTests
{
    private const string Owner = "user-A";
    private const string BlobPrefix = "develon/excavatrice-doosan-dx10z";
    private const string DocumentId = "develon-dx10z-manuel-utilisation-maintenance-en";
    private const string SourceBlob = BlobPrefix + "/DEVELON-DX10z-Manuel-Utilisation-Maintenance-EN.pdf";

    [TestMethod]
    public async Task ResolveAsync_ValidOwnedSourceReturnsPageMapSourceBlob()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.Access.ResolveAsync(fixture.SourceId, Owner, fixture.Principal, default);

        Assert.AreEqual(TechnicalSourceAccessKind.Success, result.Kind);
        Assert.AreEqual(SourceBlob, result.SourceBlob);
    }

    [TestMethod]
    public async Task ResolveAsync_UnknownSourceReturnsNotFound()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.Access.ResolveAsync(long.MaxValue, Owner, fixture.Principal, default);

        Assert.AreEqual(TechnicalSourceAccessKind.NotFound, result.Kind);
    }

    [TestMethod]
    public async Task ResolveAsync_OtherOwnerReturnsNotFound()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.Access.ResolveAsync(fixture.SourceId, "user-B", fixture.Principal, default);

        Assert.AreEqual(TechnicalSourceAccessKind.NotFound, result.Kind);
    }

    [TestMethod]
    public async Task ResolveAsync_RevokedMachineAccessReturnsForbidden()
    {
        await using var fixture = await Fixture.CreateAsync(grantMachineAccess: false);

        var result = await fixture.Access.ResolveAsync(fixture.SourceId, Owner, fixture.Principal, default);

        Assert.AreEqual(TechnicalSourceAccessKind.Forbidden, result.Kind);
    }

    [TestMethod]
    public async Task ResolveAsync_ChangedPageMapReturnsNotFound()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Reader.PageMapPdfPage = 73;

        var result = await fixture.Access.ResolveAsync(fixture.SourceId, Owner, fixture.Principal, default);

        Assert.AreEqual(TechnicalSourceAccessKind.NotFound, result.Kind);
    }

    [TestMethod]
    public async Task OpenResolvedAsync_StreamsInlinePdfWithoutStorageMetadata()
    {
        await using var fixture = await Fixture.CreateAsync();
        var pdf = Encoding.ASCII.GetBytes("%PDF-1.7 test");
        fixture.Reader.PdfContent = pdf;
        var context = HttpContext();

        var result = await TechnicalSourceEndpoints.OpenResolvedAsync(
            fixture.SourceId,
            Owner,
            fixture.Principal,
            fixture.Access,
            fixture.Reader,
            context.Response,
            default);

        Assert.AreEqual("application/pdf", ((IContentTypeHttpResult)result).ContentType);
        Assert.AreEqual("inline", context.Response.Headers.ContentDisposition.ToString());
        Assert.AreEqual("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.AreEqual(SourceBlob, fixture.Reader.RequestedPdfBlobName);

        await result.ExecuteAsync(context);
        CollectionAssert.AreEqual(pdf, ((MemoryStream)context.Response.Body).ToArray());
        var publicResponse = context.Response.Headers + Encoding.ASCII.GetString(((MemoryStream)context.Response.Body).ToArray());
        foreach (var forbidden in new[] { DocumentId, SourceBlob, BlobPrefix, "https://", "sig=", "sas" })
        {
            Assert.IsFalse(publicResponse.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
        }
    }

    [TestMethod]
    public async Task OpenResolvedAsync_MissingPdfReturnsNotFound()
    {
        await using var fixture = await Fixture.CreateAsync();
        var context = HttpContext();

        var result = await TechnicalSourceEndpoints.OpenResolvedAsync(
            fixture.SourceId,
            Owner,
            fixture.Principal,
            fixture.Access,
            fixture.Reader,
            context.Response,
            default);

        Assert.AreEqual(StatusCodes.Status404NotFound, ((IStatusCodeHttpResult)result).StatusCode);
    }

    [TestMethod]
    public async Task EndpointRequiresConfiguredAuthorizationPolicy()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization(options => options.AddPolicy("SourcePolicy", policy => policy.RequireAuthenticatedUser()));
        builder.Services.AddScoped<UserIdentityService>();
        await using var app = builder.Build();

        app.MapTechnicalSourceEndpoints("SourcePolicy");

        var endpoint = ((Microsoft.AspNetCore.Routing.IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
            .Single(item => item.RoutePattern.RawText == "/api/chat/sources/{sourceReferenceId:long}/document");
        Assert.IsTrue(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Any(metadata => metadata.Policy == "SourcePolicy"));
    }

    [TestMethod]
    public async Task DocumentEndpoint_UnauthenticatedReturnsUnauthorized()
    {
        await using var factory = new SourceEndpointApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync("/api/chat/sources/1/document");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task DocumentEndpoint_MissingSourceReturnsNotFound()
    {
        await using var factory = new SourceEndpointApplicationFactory();
        var token = factory.AddJwt(Guid.NewGuid(), Guid.NewGuid());

        using var response = await GetDocumentAsync(factory, long.MaxValue, token);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task DocumentEndpoint_OwnerWithoutMachineAccessReturnsForbidden()
    {
        await using var factory = new SourceEndpointApplicationFactory();
        var fixture = await factory.SeedAsync(grantMachineAccess: false);

        using var response = await GetDocumentAsync(factory, fixture.SourceId, fixture.Token);

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task DocumentEndpoint_OwnerWithMachineAccessStreamsInlinePdf()
    {
        await using var factory = new SourceEndpointApplicationFactory();
        var fixture = await factory.SeedAsync(grantMachineAccess: true);

        using var response = await GetDocumentAsync(factory, fixture.SourceId, fixture.Token);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.AreEqual("inline", response.Content.Headers.ContentDisposition?.DispositionType);
        CollectionAssert.AreEqual(factory.Reader.PdfContent, await response.Content.ReadAsByteArrayAsync());
    }

    [TestMethod]
    public async Task DocumentEndpoint_RangeRequestReturnsPartialContent()
    {
        await using var factory = new SourceEndpointApplicationFactory();
        var fixture = await factory.SeedAsync(grantMachineAccess: true);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/chat/sources/{fixture.SourceId}/document");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.Token);
        request.Headers.Range = new RangeHeaderValue(0, 3);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.PartialContent, response.StatusCode);
        CollectionAssert.AreEqual(factory.Reader.PdfContent![..4], await response.Content.ReadAsByteArrayAsync());
    }

    private static async Task<HttpResponseMessage> GetDocumentAsync(
        SourceEndpointApplicationFactory factory,
        long sourceId,
        string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/chat/sources/{sourceId}/document");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        return await client.SendAsync(request);
    }

    private static DefaultHttpContext HttpContext()
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
            Response = { Body = new MemoryStream() }
        };
        return context;
    }

    private sealed class FakeDocumentBlobReader : ITechnicalDocumentBlobReader
    {
        public byte[]? PdfContent { get; set; }
        public int PageMapPdfPage { get; set; } = 72;
        public string? RequestedPdfBlobName { get; private set; }

        public Task<Stream?> OpenPageMapAsync(string blobName, CancellationToken cancellationToken)
        {
            var json = $$"""
            {
              "schemaVersion": 1,
              "documentId": "{{DocumentId}}",
              "sourceBlob": "{{SourceBlob}}",
              "pages": [{"pdfPage":{{PageMapPdfPage}},"displayPage":"70","source":"ExtractedText"}]
            }
            """;
            return Task.FromResult<Stream?>(new MemoryStream(Encoding.UTF8.GetBytes(json)));
        }

        public Task<Stream?> OpenPdfAsync(string blobName, CancellationToken cancellationToken)
        {
            RequestedPdfBlobName = blobName;
            return Task.FromResult<Stream?>(PdfContent is null ? null : new MemoryStream(PdfContent));
        }
    }

    private sealed record EndpointFixture(long SourceId, string Token);

    private sealed class SourceEndpointApplicationFactory : WebApplicationFactory<BlobStorageService>
    {
        private readonly string databaseName = $"technical-source-endpoint-{Guid.NewGuid():N}";
        private readonly TestIdentityStore identities = new();

        public FakeDocumentBlobReader Reader { get; } = new()
        {
            PdfContent = Encoding.ASCII.GetBytes("%PDF-1.7 test document")
        };

        public string AddJwt(Guid companyId, Guid userId) => identities.Add(companyId, userId);

        public async Task<EndpointFixture> SeedAsync(bool grantMachineAccess)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiagLinkDbContext>();
            var companyId = Guid.NewGuid();
            var machineId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var now = DateTime.UtcNow;

            db.Companies.Add(new Company
            {
                Id = companyId,
                Name = "Company",
                Status = "active",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
            db.Machines.Add(new Machine
            {
                Id = machineId,
                CompanyId = companyId,
                Name = "DX10z",
                Status = "active",
                BlobPrefix = BlobPrefix,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
            if (grantMachineAccess)
            {
                db.UserMachineAccess.Add(new UserMachineAccess
                {
                    UserId = userId,
                    MachineId = machineId,
                    CreatedAtUtc = now
                });
            }

            var source = new ConversationMessageSourceReference
            {
                ConversationMessage = new ConversationMessage
                {
                    Conversation = new Conversation
                    {
                        Id = Guid.NewGuid(),
                        ConversationPublicId = $"conversation-{Guid.NewGuid():N}",
                        UserObjectId = userId.ToString(),
                        MachineId = machineId,
                        CreatedAtUtc = now,
                        UpdatedAtUtc = now
                    },
                    Role = "assistant",
                    Content = "Source : p. 70.",
                    CreatedAtUtc = now
                },
                DocumentId = DocumentId,
                PdfPage = 72,
                DisplayPage = "70",
                Label = "p. 70",
                StartIndex = 9,
                EndIndex = 14,
                DisplayOrder = 0
            };
            db.ConversationMessageSourceReferences.Add(source);
            await db.SaveChangesAsync();

            return new(source.Id, AddJwt(companyId, userId));
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DiagLink"] = "Server=(local);Database=technical-source-tests",
                    ["AZURE_STORAGE_CONNECTION_STRING"] = string.Empty
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveWindowsEventLogProvider();
                services.RemoveAll<DiagLinkDbContext>();
                services.RemoveAll<DbContextOptions<DiagLinkDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<DiagLinkDbContext>>();
                services.AddDbContext<DiagLinkDbContext>(options => options.UseInMemoryDatabase(databaseName));

                services.RemoveAll<ITechnicalDocumentBlobReader>();
                services.RemoveAll<TechnicalPageMapResolver>();
                services.RemoveAll<TechnicalSourceAccessService>();
                services.AddSingleton(Reader);
                services.AddSingleton<ITechnicalDocumentBlobReader>(Reader);
                services.AddScoped<TechnicalPageMapResolver>();
                services.AddScoped<TechnicalSourceAccessService>();
                services.AddSingleton<IMachineRequestBlobClient, UnusedMachineRequestBlobClient>();
                services.AddScoped<MachineRequestStorageService>();

                services.RemoveAll<IConfigureOptions<AuthenticationOptions>>();
                services.AddSingleton(identities);
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

    private sealed record TestIdentity(Guid CompanyId, Guid UserId);

    private sealed class TestIdentityStore
    {
        private readonly ConcurrentDictionary<string, TestIdentity> identities = new();

        public string Add(Guid companyId, Guid userId)
        {
            var token = Guid.NewGuid().ToString("N");
            identities[token] = new(companyId, userId);
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
                new("oid", identity.UserId.ToString()),
                new("scp", ChatAccessRequirement.RequiredScope),
                new(DiagLinkClaimTypes.UserId, identity.UserId.ToString()),
                new(DiagLinkClaimTypes.CompanyId, identity.CompanyId.ToString()),
                new(ClaimTypes.Role, DiagLinkRoles.Technician)
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

    private sealed class UnusedMachineRequestBlobClient : IMachineRequestBlobClient
    {
        public Task EnsurePrivateContainerAsync(CancellationToken cancellationToken = default) =>
            throw new AssertFailedException("Technical source endpoint tests must not access machine request storage.");

        public Task UploadAsync(
            string blobName,
            Stream content,
            string contentType,
            bool overwrite,
            CancellationToken cancellationToken = default) =>
            throw new AssertFailedException("Technical source endpoint tests must not access machine request storage.");

        public Task<Stream?> OpenReadAsync(string blobName, CancellationToken cancellationToken = default) =>
            throw new AssertFailedException("Technical source endpoint tests must not access machine request storage.");

        public IAsyncEnumerable<string> ListNamesAsync(
            string? prefix = null,
            CancellationToken cancellationToken = default) =>
            throw new AssertFailedException("Technical source endpoint tests must not access machine request storage.");

        public Task DeletePrefixAsync(string prefix, CancellationToken cancellationToken = default) =>
            throw new AssertFailedException("Technical source endpoint tests must not access machine request storage.");
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly DiagLinkDbContext db;

        private Fixture(
            DiagLinkDbContext db,
            TechnicalSourceAccessService access,
            FakeDocumentBlobReader reader,
            ClaimsPrincipal principal,
            long sourceId)
        {
            this.db = db;
            Access = access;
            Reader = reader;
            Principal = principal;
            SourceId = sourceId;
        }

        public TechnicalSourceAccessService Access { get; }
        public FakeDocumentBlobReader Reader { get; }
        public ClaimsPrincipal Principal { get; }
        public long SourceId { get; }

        public static async Task<Fixture> CreateAsync(bool grantMachineAccess = true)
        {
            var options = new DbContextOptionsBuilder<DiagLinkDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var db = new DiagLinkDbContext(options);
            var companyId = Guid.NewGuid();
            var machineId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var now = DateTime.UtcNow;

            db.Companies.Add(new Company
            {
                Id = companyId,
                Name = "Company",
                Status = "active",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
            db.Machines.Add(new Machine
            {
                Id = machineId,
                CompanyId = companyId,
                Name = "DX10z",
                Status = "active",
                BlobPrefix = BlobPrefix,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
            if (grantMachineAccess)
            {
                db.UserMachineAccess.Add(new UserMachineAccess
                {
                    UserId = userId,
                    MachineId = machineId,
                    CreatedAtUtc = now
                });
            }

            var source = new ConversationMessageSourceReference
            {
                ConversationMessage = new ConversationMessage
                {
                    Conversation = new Conversation
                    {
                        Id = Guid.NewGuid(),
                        ConversationPublicId = "conversation",
                        UserObjectId = Owner,
                        MachineId = machineId,
                        CreatedAtUtc = now,
                        UpdatedAtUtc = now
                    },
                    Role = "assistant",
                    Content = "Source : p. 70.",
                    CreatedAtUtc = now
                },
                DocumentId = DocumentId,
                PdfPage = 72,
                DisplayPage = "70",
                Label = "p. 70",
                StartIndex = 9,
                EndIndex = 14,
                DisplayOrder = 0
            };
            db.ConversationMessageSourceReferences.Add(source);
            await db.SaveChangesAsync();

            var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Role, DiagLinkRoles.Technician),
                new Claim(DiagLinkClaimTypes.CompanyId, companyId.ToString()),
                new Claim(DiagLinkClaimTypes.UserId, userId.ToString())
            ], "test"));
            var reader = new FakeDocumentBlobReader();
            var pageMapResolver = new TechnicalPageMapResolver(
                reader,
                NullLogger<TechnicalPageMapResolver>.Instance);
            var access = new TechnicalSourceAccessService(
                db,
                new MachineAccessService(db),
                pageMapResolver,
                NullLogger<TechnicalSourceAccessService>.Instance);
            return new(db, access, reader, principal, source.Id);
        }

        public ValueTask DisposeAsync() => db.DisposeAsync();
    }
}