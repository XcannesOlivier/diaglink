using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Data;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

[TestClass]
public sealed class CompanyBrandingServiceTests
{
    [TestMethod]
    public async Task GetAsync_WithoutBranding_ReturnsNull()
    {
        await using var fixture = await Fixture.CreateAsync();

        Assert.IsNull(await fixture.Service.GetAsync(fixture.CompanyA));
    }

    [TestMethod]
    public async Task SetAccentColorAsync_CreatesAndNormalizesValidColor()
    {
        await using var fixture = await Fixture.CreateAsync();

        var branding = await fixture.Service.SetAccentColorAsync(fixture.CompanyA, "#a1b2c3");

        Assert.IsNotNull(branding);
        Assert.AreEqual("#A1B2C3", branding.AccentColor);
        Assert.AreEqual(fixture.Clock.GetUtcNow().UtcDateTime, branding.UpdatedAtUtc);
    }

    [TestMethod]
    public async Task SetAccentColorAsync_UpdatesExistingColor()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.SetAccentColorAsync(fixture.CompanyA, "#112233");
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));

        var branding = await fixture.Service.SetAccentColorAsync(fixture.CompanyA, "#445566");

        Assert.IsNotNull(branding);
        Assert.AreEqual("#445566", branding.AccentColor);
        Assert.AreEqual(1, await fixture.Db.CompanyBrandings.CountAsync());
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("112233")]
    [DataRow("#123")]
    [DataRow("#1234567")]
    [DataRow("#GGGGGG")]
    [DataRow(" #123456")]
    public async Task SetAccentColorAsync_RejectsInvalidColor(string color)
    {
        await using var fixture = await Fixture.CreateAsync();

        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => fixture.Service.SetAccentColorAsync(fixture.CompanyA, color));
        Assert.AreEqual(0, await fixture.Db.CompanyBrandings.CountAsync());
    }

    [TestMethod]
    public async Task SetAccentColorAsync_NullRestoresDefaultColorAndKeepsLogo()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.SetAccentColorAsync(fixture.CompanyA, "#123456");
        await fixture.Service.SetLogoMetadataAsync(fixture.CompanyA, "companies/logo.png", "image/png");

        var branding = await fixture.Service.SetAccentColorAsync(fixture.CompanyA, null);

        Assert.IsNotNull(branding);
        Assert.IsNull(branding.AccentColor);
        Assert.AreEqual("companies/logo.png", branding.LogoBlobName);
    }

    [TestMethod]
    public async Task SetLogoMetadataAsync_StoresOnlyInternalMetadata()
    {
        await using var fixture = await Fixture.CreateAsync();

        var branding = await fixture.Service.SetLogoMetadataAsync(
            fixture.CompanyA,
            $"companies/{fixture.CompanyA:N}/logo.png",
            "image/png");

        Assert.AreEqual($"companies/{fixture.CompanyA:N}/logo.png", branding.LogoBlobName);
        Assert.AreEqual("image/png", branding.LogoContentType);
        Assert.IsNull(branding.AccentColor);
    }

    [TestMethod]
    public async Task ClearLogoMetadataAsync_ClearsLogoAndKeepsAccent()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.SetAccentColorAsync(fixture.CompanyA, "#123456");
        await fixture.Service.SetLogoMetadataAsync(fixture.CompanyA, "companies/logo.png", "image/png");

        var branding = await fixture.Service.ClearLogoMetadataAsync(fixture.CompanyA);

        Assert.IsNotNull(branding);
        Assert.AreEqual("#123456", branding.AccentColor);
        Assert.IsNull(branding.LogoBlobName);
        Assert.IsNull(branding.LogoContentType);
    }

    [TestMethod]
    public async Task ResetAsync_RemovesBrandingRow()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.SetAccentColorAsync(fixture.CompanyA, "#123456");
        await fixture.Service.SetLogoMetadataAsync(fixture.CompanyA, "companies/logo.png", "image/png");

        await fixture.Service.ResetAsync(fixture.CompanyA);

        Assert.IsNull(await fixture.Service.GetAsync(fixture.CompanyA));
        Assert.AreEqual(0, await fixture.Db.CompanyBrandings.CountAsync());
    }

    [TestMethod]
    public async Task Mutations_UpdateUpdatedAtUtc()
    {
        await using var fixture = await Fixture.CreateAsync();
        var created = await fixture.Service.SetAccentColorAsync(fixture.CompanyA, "#123456");
        var firstUpdate = created!.UpdatedAtUtc;
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));

        var withLogo = await fixture.Service.SetLogoMetadataAsync(fixture.CompanyA, "companies/logo.png", "image/png");
        var secondUpdate = withLogo.UpdatedAtUtc;
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        var withoutLogo = await fixture.Service.ClearLogoMetadataAsync(fixture.CompanyA);

        Assert.IsTrue(secondUpdate > firstUpdate);
        Assert.IsNotNull(withoutLogo);
        Assert.IsTrue(withoutLogo.UpdatedAtUtc > secondUpdate);
        Assert.AreEqual(fixture.Clock.GetUtcNow().UtcDateTime, withoutLogo.UpdatedAtUtc);
    }

    [TestMethod]
    public async Task Mutations_AreIsolatedByCompanyId()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.SetAccentColorAsync(fixture.CompanyA, "#123456");
        await fixture.Service.SetLogoMetadataAsync(fixture.CompanyB, "companies/b/logo.png", "image/png");

        await fixture.Service.SetAccentColorAsync(fixture.CompanyA, "#ABCDEF");

        var companyA = await fixture.Service.GetAsync(fixture.CompanyA);
        var companyB = await fixture.Service.GetAsync(fixture.CompanyB);
        Assert.AreEqual("#ABCDEF", companyA!.AccentColor);
        Assert.IsNull(companyA.LogoBlobName);
        Assert.IsNull(companyB!.AccentColor);
        Assert.AreEqual("companies/b/logo.png", companyB.LogoBlobName);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(DiagLinkDbContext db, MutableTimeProvider clock, Guid companyA, Guid companyB)
        {
            Db = db;
            Clock = clock;
            CompanyA = companyA;
            CompanyB = companyB;
            Service = new CompanyBrandingService(db, clock);
        }

        public DiagLinkDbContext Db { get; }
        public MutableTimeProvider Clock { get; }
        public Guid CompanyA { get; }
        public Guid CompanyB { get; }
        public CompanyBrandingService Service { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var options = new DbContextOptionsBuilder<DiagLinkDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var db = new DiagLinkDbContext(options);
            var now = new DateTimeOffset(2026, 10, 9, 8, 0, 0, TimeSpan.Zero);
            var companyA = Guid.NewGuid();
            var companyB = Guid.NewGuid();
            db.Companies.AddRange(
                new Company { Id = companyA, Name = "Company A", Status = "active", CreatedAtUtc = now.UtcDateTime, UpdatedAtUtc = now.UtcDateTime },
                new Company { Id = companyB, Name = "Company B", Status = "active", CreatedAtUtc = now.UtcDateTime, UpdatedAtUtc = now.UtcDateTime });
            await db.SaveChangesAsync();
            return new Fixture(db, new MutableTimeProvider(now), companyA, companyB);
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
        public void Advance(TimeSpan duration) => utcNow = utcNow.Add(duration);
    }
}
