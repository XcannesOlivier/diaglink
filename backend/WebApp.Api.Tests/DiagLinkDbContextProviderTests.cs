using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Data;

namespace WebApp.Api.Tests;

[TestClass]
public sealed class DiagLinkDbContextProviderTests
{
    [TestMethod]
    public async Task EnsureCreatedAsync_WithSqlite_CreatesSchemaWithoutSqlServerSpecificConstraint()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<DiagLinkDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new DiagLinkDbContext(options);

        Assert.IsTrue(await db.Database.EnsureCreatedAsync());
    }
}
