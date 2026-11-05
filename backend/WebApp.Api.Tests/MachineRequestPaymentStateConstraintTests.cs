using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Data;
using WebApp.Api.Migrations.PostgreSql.Migrations;
using WebApp.Api.Models.Entities;

namespace WebApp.Api.Tests;

[TestClass]
public class MachineRequestPaymentStateConstraintTests
{
    private const string ExpectedStateSql = """("Status" = 0 AND "StripePaymentIntentId" IS NULL AND "AuthorizationEventId" IS NULL AND "AuthorizedAtUtc" IS NULL AND "CapturedAtUtc" IS NULL AND "CancelledAtUtc" IS NULL) OR ("Status" = 1 AND "StripePaymentIntentId" IS NOT NULL AND "AuthorizationEventId" IS NOT NULL AND "AuthorizedAtUtc" IS NOT NULL AND "CapturedAtUtc" IS NULL AND "CancelledAtUtc" IS NULL) OR ("Status" = 2 AND "StripePaymentIntentId" IS NOT NULL AND "AuthorizationEventId" IS NOT NULL AND "AuthorizedAtUtc" IS NOT NULL AND "CapturedAtUtc" IS NOT NULL AND "CancelledAtUtc" IS NULL) OR ("Status" = 3 AND "StripePaymentIntentId" IS NOT NULL AND "AuthorizationEventId" IS NOT NULL AND "AuthorizedAtUtc" IS NOT NULL AND "CapturedAtUtc" IS NULL AND "CancelledAtUtc" IS NOT NULL) OR ("Status" = 4 AND "StripePaymentIntentId" IS NULL AND "AuthorizationEventId" IS NULL AND "AuthorizedAtUtc" IS NULL AND "CapturedAtUtc" IS NULL AND "CancelledAtUtc" IS NOT NULL)""";
    private const string ExpectedStatusSql = "\"Status\" >= 0 AND \"Status\" <= 4";

    [TestMethod]
    public void PostgreSqlModelAndMigrationExposeTheExactAbandonedConstraint()
    {
        using var db = new DiagLinkDbContext(new DbContextOptionsBuilder<DiagLinkDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=15439;Database=constraint_metadata_only;Username=test;Password=test")
            .Options);
        var entity = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(MachineRequestPayment))!;
        Assert.AreEqual(ExpectedStateSql,
            entity.GetCheckConstraints().Single(item => item.Name == "CK_MachineRequestPayments_State").Sql);
        Assert.AreEqual(ExpectedStatusSql,
            entity.GetCheckConstraints().Single(item => item.Name == "CK_MachineRequestPayments_Status").Sql);

        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        typeof(InitialPostgreSql)
            .GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(new InitialPostgreSql(), [builder]);
        var table = builder.Operations.OfType<CreateTableOperation>()
            .Single(item => item.Schema == "dbo" && item.Name == "MachineRequestPayments");
        var constraints = table.CheckConstraints.ToDictionary(item => item.Name);
        Assert.AreEqual(ExpectedStateSql, constraints["CK_MachineRequestPayments_State"].Sql);
        Assert.AreEqual(ExpectedStatusSql, constraints["CK_MachineRequestPayments_Status"].Sql);
    }

    [TestMethod]
    [DataRow(MachineRequestPaymentStatus.Abandoned, false, false, false, false, true, true)]
    [DataRow(MachineRequestPaymentStatus.Abandoned, true, false, false, false, true, false)]
    [DataRow(MachineRequestPaymentStatus.Abandoned, false, false, true, false, true, false)]
    [DataRow(MachineRequestPaymentStatus.Cancelled, true, true, true, false, true, true)]
    [DataRow(MachineRequestPaymentStatus.Cancelled, false, false, false, false, true, false)]
    [DataRow(MachineRequestPaymentStatus.Captured, true, true, true, true, false, true)]
    public void StateMatrixKeepsAbandonmentDistinctFromStripeCancellation(
        MachineRequestPaymentStatus status, bool intent, bool authorizationEvent, bool authorizedAt,
        bool capturedAt, bool terminalAt, bool expected)
    {
        var valid = status switch
        {
            MachineRequestPaymentStatus.Abandoned => !intent && !authorizationEvent && !authorizedAt && !capturedAt && terminalAt,
            MachineRequestPaymentStatus.Cancelled => intent && authorizationEvent && authorizedAt && !capturedAt && terminalAt,
            MachineRequestPaymentStatus.Captured => intent && authorizationEvent && authorizedAt && capturedAt && !terminalAt,
            _ => false
        };
        Assert.AreEqual(expected, valid);
    }
}
