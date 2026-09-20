using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Models.Entities;
using Fixture = WebApp.Api.Tests.AiCreditConsumptionServiceTests.Fixture;

namespace WebApp.Api.Tests;

[TestClass]
public class CreditLedgerExternalEventTests
{
    private static CreditLedgerEntry Entry(Guid companyId, string? eventId) => new()
    {
        Id = Guid.NewGuid(), CompanyId = companyId, EntryType = "TestEvent",
        BucketType = "CompanyWallet", ExternalEventId = eventId, Currency = "EUR"
    };

    [TestMethod]
    public async Task NullAndDistinctEventIdsAreAllowed()
    {
        await using var fixture = new Fixture();
        await fixture.Seed();
        await using var db = fixture.Db();
        db.CreditLedger.AddRange(Entry(fixture.CompanyId, null), Entry(fixture.CompanyId, null),
            Entry(fixture.CompanyId, "evt_one"), Entry(fixture.CompanyId, "evt_two"));
        await db.SaveChangesAsync();
        Assert.AreEqual(4, await db.CreditLedger.CountAsync());
        var index = db.Model.FindEntityType(typeof(CreditLedgerEntry))!.GetIndexes()
            .Single(i => i.Properties.Count == 1 && i.Properties[0].Name == nameof(CreditLedgerEntry.ExternalEventId));
        Assert.IsTrue(index.IsUnique);
        Assert.AreEqual("[ExternalEventId] IS NOT NULL", index.GetFilter());
    }

    [TestMethod]
    public async Task DuplicateEventIsRejectedEvenForAnotherBucket()
    {
        await using var fixture = new Fixture();
        await fixture.Seed();
        await using (var db = fixture.Db())
        {
            db.CreditLedger.Add(Entry(fixture.CompanyId, "evt_duplicate"));
            await db.SaveChangesAsync();
        }
        await using (var db = fixture.Db())
        {
            var duplicate = Entry(fixture.CompanyId, "evt_duplicate");
            duplicate.BucketType = "MachineIncluded";
            db.CreditLedger.Add(duplicate);
            await Assert.ThrowsExactlyAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        await using var check = fixture.Db();
        Assert.AreEqual(1, await check.CreditLedger.CountAsync());
    }
}
