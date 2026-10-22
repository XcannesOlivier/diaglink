using System.Security.Claims;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WebApp.Api.Data;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

[TestClass]
public sealed class DocumentIngestionFinalizationTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task FinalizeAsync_EmptyConfiguration_UpdatesExpectedMachine()
    {
        await using var fixture = await Fixture.CreateAsync();

        var outcome = await fixture.Service.FinalizeAsync(Fixture.ValidRequest, default);

        Assert.AreEqual(DocumentIngestionFinalizationKind.Updated, outcome.Kind);
        var machine = await fixture.Db.Machines.AsNoTracking().SingleAsync();
        Assert.AreEqual(Fixture.ValidRequest.ProjectEndpoint, machine.ProjectEndpoint);
        Assert.AreEqual(Fixture.ValidRequest.VectorStoreId, machine.VectorStoreId);
        Assert.AreEqual(FixedNow.UtcDateTime, machine.UpdatedAtUtc);
    }

    [TestMethod]
    public async Task FinalizeAsync_SameConfiguration_IsIdempotent()
    {
        await using var fixture = await Fixture.CreateAsync(
            projectEndpoint: Fixture.ValidRequest.ProjectEndpoint,
            vectorStoreId: Fixture.ValidRequest.VectorStoreId);
        var originalUpdatedAt = (await fixture.Db.Machines.AsNoTracking().SingleAsync()).UpdatedAtUtc;

        var outcome = await fixture.Service.FinalizeAsync(Fixture.ValidRequest, default);

        Assert.AreEqual(DocumentIngestionFinalizationKind.AlreadyFinalized, outcome.Kind);
        Assert.AreEqual(
            originalUpdatedAt,
            (await fixture.Db.Machines.AsNoTracking().SingleAsync()).UpdatedAtUtc);
    }

    [TestMethod]
    public async Task FinalizeAsync_DifferentExistingConfiguration_RefusesReplacement()
    {
        const string doosanVectorStoreId = "vs_DoosanExisting1777Files";
        await using var fixture = await Fixture.CreateAsync(
            projectEndpoint: Fixture.ValidRequest.ProjectEndpoint,
            vectorStoreId: doosanVectorStoreId);

        var outcome = await fixture.Service.FinalizeAsync(Fixture.ValidRequest, default);

        Assert.AreEqual(DocumentIngestionFinalizationKind.ConfigurationConflict, outcome.Kind);
        Assert.AreEqual(
            doosanVectorStoreId,
            (await fixture.Db.Machines.AsNoTracking().SingleAsync()).VectorStoreId);
    }

    [TestMethod]
    public async Task FinalizeAsync_BlobPrefixDoesNotMatchCompany_RefusesUpdate()
    {
        await using var fixture = await Fixture.CreateAsync(companyName: "Une autre entreprise");

        var outcome = await fixture.Service.FinalizeAsync(Fixture.ValidRequest, default);

        Assert.AreEqual(DocumentIngestionFinalizationKind.CompanyMismatch, outcome.Kind);
        Assert.IsNull((await fixture.Db.Machines.AsNoTracking().SingleAsync()).VectorStoreId);
    }

    [TestMethod]
    public async Task FinalizeAsync_ProjectDoesNotMatchCompanyPrefix_IsInvalid()
    {
        await using var fixture = await Fixture.CreateAsync();
        var request = Fixture.ValidRequest with
        {
            ProjectEndpoint = "https://foundry.services.ai.azure.com/api/projects/other-company",
        };

        var outcome = await fixture.Service.FinalizeAsync(request, default);

        Assert.AreEqual(DocumentIngestionFinalizationKind.InvalidRequest, outcome.Kind);
    }

    [TestMethod]
    public void ManagedIdentityAuthorization_RequiresRoleAndConfiguredObjectId()
    {
        var allowedObjectId = Guid.NewGuid();
        var principal = Principal(allowedObjectId, DocumentIngestionAuthorization.AppRole);

        Assert.IsTrue(DocumentIngestionAuthorization.IsAllowedCaller(
            principal,
            allowedObjectId.ToString()));
        Assert.IsFalse(DocumentIngestionAuthorization.IsAllowedCaller(
            principal,
            Guid.NewGuid().ToString()));
        Assert.IsFalse(DocumentIngestionAuthorization.IsAllowedCaller(
            Principal(allowedObjectId, "Other.Role"),
            allowedObjectId.ToString()));
        Assert.IsFalse(DocumentIngestionAuthorization.IsAllowedCaller(principal, null));
    }

    private static ClaimsPrincipal Principal(Guid objectId, string role) => new(
        new ClaimsIdentity(
            [new Claim("oid", objectId.ToString()), new Claim(ClaimTypes.Role, role)],
            "test"));

    private sealed class Fixture : IAsyncDisposable
    {
        public static readonly DocumentIngestionFinalizationRequest ValidRequest = new(
            "sarl-rocher/doosan-dx225lca",
            "https://foundry.services.ai.azure.com/api/projects/sarl-rocher",
            "vs_NewMachineConfiguration");

        private readonly SqliteConnection connection;

        private Fixture(SqliteConnection connection, DiagLinkDbContext db)
        {
            this.connection = connection;
            Db = db;
            Service = new(db, new FixedTimeProvider(FixedNow));
        }

        public DiagLinkDbContext Db { get; }
        public DocumentIngestionFinalizationService Service { get; }

        public static async Task<Fixture> CreateAsync(
            string companyName = "SARL ROCHER",
            string? projectEndpoint = null,
            string? vectorStoreId = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<DiagLinkDbContext>()
                .UseSqlite(connection)
                .Options;
            var db = new DiagLinkDbContext(options);
            await db.Database.EnsureCreatedAsync();
            // These four legacy SQL Server tables are excluded from non-PostgreSQL migrations.
            // Create only the two tables required by this relational SQLite test fixture.
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS Companies (
                    Id TEXT PRIMARY KEY,
                    Name TEXT NOT NULL,
                    Status TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );
                """);
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS Machines (
                    Id TEXT PRIMARY KEY,
                    CompanyId TEXT NOT NULL,
                    Name TEXT NOT NULL,
                    Status TEXT NOT NULL,
                    Reference TEXT,
                    VectorStoreId TEXT,
                    BlobPrefix TEXT,
                    ProjectEndpoint TEXT,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );
                """);

            var companyId = Guid.NewGuid();
            db.Companies.Add(new Company
            {
                Id = companyId,
                Name = companyName,
                Status = "active",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            });
            db.Machines.Add(new Machine
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                Name = "Doosan DX225LCA",
                Status = "active",
                BlobPrefix = ValidRequest.BlobPrefix,
                ProjectEndpoint = projectEndpoint,
                VectorStoreId = vectorStoreId,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            });
            await db.SaveChangesAsync();
            return new Fixture(connection, db);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
