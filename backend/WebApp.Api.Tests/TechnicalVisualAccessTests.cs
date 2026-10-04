using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Data;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

[TestClass]
public class TechnicalVisualAccessTests
{
    private const string Owner = "user-A";
    private const string AssetKey = "manual/page-00071/manual_page-00071-full.png";
    private const string BlobPrefix = "company/machine";

    [TestMethod]
    public async Task ResolveAsync_ValidOwnedVisualReturnsAuthorizedBlobName()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.Access.ResolveAsync(fixture.VisualId, Owner, fixture.Principal, default);

        Assert.AreEqual(TechnicalVisualAccessKind.Success, result.Kind);
        Assert.AreEqual($"{BlobPrefix}/{AssetKey}", result.BlobName);
    }

    [TestMethod]
    public async Task ResolveAsync_UnknownVisualReturnsNotFound()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.Access.ResolveAsync(long.MaxValue, Owner, fixture.Principal, default);

        Assert.AreEqual(TechnicalVisualAccessKind.NotFound, result.Kind);
    }

    [TestMethod]
    public async Task ResolveAsync_OtherUsersVisualReturnsNotFound()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.Access.ResolveAsync(fixture.VisualId, "user-B", fixture.Principal, default);

        Assert.AreEqual(TechnicalVisualAccessKind.NotFound, result.Kind);
    }

    [TestMethod]
    public async Task ResolveAsync_RevokedMachineAccessReturnsForbidden()
    {
        await using var fixture = await Fixture.CreateAsync(grantMachineAccess: false);

        var result = await fixture.Access.ResolveAsync(fixture.VisualId, Owner, fixture.Principal, default);

        Assert.AreEqual(TechnicalVisualAccessKind.Forbidden, result.Kind);
    }

    [TestMethod]
    public async Task ResolveAsync_LegacyConversationWithoutMachineReturnsNotFound()
    {
        await using var fixture = await Fixture.CreateAsync(conversationHasMachine: false);

        var result = await fixture.Access.ResolveAsync(fixture.VisualId, Owner, fixture.Principal, default);

        Assert.AreEqual(TechnicalVisualAccessKind.NotFound, result.Kind);
    }

    [TestMethod]
    public void TryBuildBlobName_UsesMachinePrefixAndRelativeAssetKey()
    {
        Assert.IsTrue(TechnicalVisualAccessService.TryBuildBlobName(BlobPrefix, AssetKey, out var blobName));
        Assert.AreEqual($"{BlobPrefix}/{AssetKey}", blobName);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("/manual/image.png")]
    [DataRow("\\manual\\image.png")]
    [DataRow("manual/../image.png")]
    [DataRow("manual\\image.png")]
    [DataRow("https://storage.test/image.png")]
    [DataRow("file:manual/image.png")]
    [DataRow("manual/image.jpg")]
    public void TryBuildBlobName_RejectsUnsafeAssetKey(string assetKey)
    {
        Assert.IsFalse(TechnicalVisualAccessService.TryBuildBlobName(BlobPrefix, assetKey, out _));
    }

    [TestMethod]
    public async Task OpenResolvedAsync_MissingBlobReturnsNotFound()
    {
        await using var fixture = await Fixture.CreateAsync();
        var context = HttpContext();

        var result = await TechnicalVisualEndpoints.OpenResolvedAsync(
            fixture.VisualId, Owner, fixture.Principal, fixture.Access, new FakeBlobReader(null), context.Response, default);

        Assert.AreEqual(StatusCodes.Status404NotFound, ((IStatusCodeHttpResult)result).StatusCode);
    }

    [TestMethod]
    public async Task OpenResolvedAsync_StreamsPngWithNoStoreAndNoMetadataExposure()
    {
        await using var fixture = await Fixture.CreateAsync();
        var png = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        var reader = new FakeBlobReader(png);
        var context = HttpContext();

        var result = await TechnicalVisualEndpoints.OpenResolvedAsync(
            fixture.VisualId, Owner, fixture.Principal, fixture.Access, reader, context.Response, default);

        Assert.AreEqual("image/png", ((IContentTypeHttpResult)result).ContentType);
        Assert.AreEqual("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.AreEqual($"{BlobPrefix}/{AssetKey}", reader.RequestedBlobName);

        await result.ExecuteAsync(context);
        CollectionAssert.AreEqual(png, ((MemoryStream)context.Response.Body).ToArray());
        var publicResponse = context.Response.Headers + Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());
        Assert.IsFalse(publicResponse.Contains(AssetKey, StringComparison.Ordinal));
        Assert.IsFalse(publicResponse.Contains(BlobPrefix, StringComparison.Ordinal));
        Assert.IsFalse(publicResponse.Contains("https://", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(publicResponse.Contains("sig=", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task ResolveAsync_DoesNotLogVisualMetadataOrBlobPath()
    {
        const string sensitiveAssetKey = "https://storage.test/PRIVATE_DOCUMENT/PRIVATE_NAME.png";
        await using var fixture = await Fixture.CreateAsync(assetKey: sensitiveAssetKey);

        var result = await fixture.Access.ResolveAsync(fixture.VisualId, Owner, fixture.Principal, default);

        Assert.AreEqual(TechnicalVisualAccessKind.NotFound, result.Kind);
        var logs = string.Join("\n", fixture.Logger.Lines);
        foreach (var sensitive in new[] { sensitiveAssetKey, "PRIVATE_DOCUMENT", "PRIVATE_NAME", BlobPrefix })
        {
            Assert.IsFalse(logs.Contains(sensitive, StringComparison.Ordinal));
        }
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

    private sealed class FakeBlobReader(byte[]? content) : ITechnicalVisualBlobReader
    {
        public string? RequestedBlobName { get; private set; }

        public Task<Stream?> OpenReadAsync(string blobName, CancellationToken cancellationToken)
        {
            RequestedBlobName = blobName;
            return Task.FromResult<Stream?>(content is null ? null : new MemoryStream(content));
        }
    }

    private sealed class CaptureLogger : ILogger<TechnicalVisualAccessService>
    {
        public List<string> Lines { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Lines.Add(formatter(state, exception));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly DiagLinkDbContext db;

        private Fixture(
            DiagLinkDbContext db,
            TechnicalVisualAccessService access,
            ClaimsPrincipal principal,
            long visualId,
            CaptureLogger logger)
        {
            this.db = db;
            Access = access;
            Principal = principal;
            VisualId = visualId;
            Logger = logger;
        }

        public TechnicalVisualAccessService Access { get; }
        public ClaimsPrincipal Principal { get; }
        public long VisualId { get; }
        public CaptureLogger Logger { get; }

        public static async Task<Fixture> CreateAsync(
            bool grantMachineAccess = true,
            bool conversationHasMachine = true,
            string assetKey = AssetKey)
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
                Name = "Machine",
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

            var conversation = new Conversation
            {
                Id = Guid.NewGuid(),
                FoundryConversationId = "conversation",
                UserObjectId = Owner,
                MachineId = conversationHasMachine ? machineId : null,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            var message = new ConversationMessage
            {
                Conversation = conversation,
                Role = "assistant",
                Content = "answer",
                CreatedAtUtc = now
            };
            var visual = new ConversationMessageVisual
            {
                ConversationMessage = message,
                DocumentId = "PRIVATE_DOCUMENT",
                Page = 71,
                AssetType = "full",
                Name = "PRIVATE_NAME.png",
                AssetKey = assetKey,
                DisplayOrder = 0
            };
            db.ConversationMessageVisuals.Add(visual);
            await db.SaveChangesAsync();

            var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Role, DiagLinkRoles.Technician),
                new Claim(DiagLinkClaimTypes.CompanyId, companyId.ToString()),
                new Claim(DiagLinkClaimTypes.UserId, userId.ToString())
            ], "test"));
            var logger = new CaptureLogger();
            var access = new TechnicalVisualAccessService(db, new MachineAccessService(db), logger);
            return new Fixture(db, access, principal, visual.Id, logger);
        }

        public ValueTask DisposeAsync() => db.DisposeAsync();
    }
}
