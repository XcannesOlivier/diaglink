using System.ClientModel.Primitives;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenAI.Responses;
using WebApp.Api.Data;
using WebApp.Api.Models;
using WebApp.Api.Repositories;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;
#pragma warning disable OPENAI001

[TestClass]
public class VisionPricingCaptureTests
{
    [TestMethod]
    public async Task ProductionEnvelope_PreservesEachCallThroughSqlRepository()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<DiagLinkDbContext>().UseSqlite(connection).Options;
        await using var db = new DiagLinkDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var repository = new AiUsageRepository(options, NullLogger<AiUsageRepository>.Instance);
        var probe = new VisionToolDiagnostics(NullLogger.Instance);
        // The observed production envelope is output(string) -> response(string) -> usage.
        // Identity fields below represent the corrected source contract, not historical output.
        var identities = new (string? Provider, string? Model, string? Deployment)[]
        {
            ("Anthropic", "returned-model-a", "deployment-a"),
            ("OtherProvider", "returned-model-b", "deployment-b"),
            (null, null, null)
        };
        for (var i = 0; i < identities.Length; i++)
        {
            var identity = identities[i];
            var payload = new Dictionary<string, object?>
            {
                ["page"] = "00001", ["answer"] = "redacted",
                ["usage"] = new { input_tokens = 9708 + i, output_tokens = 653 + i },
                ["content_types"] = new[] { "text" }, ["stop_reason"] = "end_turn", ["images_sent"] = 1
            };
            if (identity.Provider != null)
            {
                payload["provider"] = identity.Provider;
                payload["model"] = identity.Model;
                payload["deployment"] = identity.Deployment;
            }
            var wire = JsonSerializer.Serialize(new
            {
                type = "openapi_call_output", id = "item_" + i, call_id = "call_" + i,
                name = "blob_page_images_analyze_page", status = "completed",
                output = JsonSerializer.Serialize(new { response = JsonSerializer.Serialize(payload) })
            });
            var item = ModelReaderWriter.Read<ResponseItem>(BinaryData.FromString(wire))!;
            var capture = probe.Observe(item, "done", "parent")!;
            Assert.AreEqual(identity.Provider, capture.Usage.Provider);
            Assert.AreEqual(identity.Model, capture.Usage.Model);
            Assert.AreEqual(identity.Deployment, capture.Usage.Deployment);
            var context = new AiUsageMeasurement(null, null, null, null, "conversation", null, capture.Usage);
            await repository.RecordAsync(capture.WithContext(context), CancellationToken.None);
            var row = await db.AiUsageRecords.AsNoTracking().SingleAsync(r => r.Id == capture.EventId);
            Assert.AreEqual(identity.Provider, row.Provider);
            Assert.AreEqual(identity.Model, row.Model);
            Assert.AreEqual(identity.Deployment, row.Deployment);
            Assert.AreEqual("call_" + i, row.CallId);
            Assert.AreEqual("parent", row.ParentResponseId);
            Assert.IsNull(row.ResponseId);
            Assert.AreEqual(9708 + i, row.InputTokens);
            Assert.AreEqual(653 + i, row.OutputTokens);
            Assert.AreEqual(10361 + 2 * i, row.TotalTokens);
            Assert.IsTrue(row.Available && row.Completed);
        }
        Assert.AreEqual(3, await db.AiUsageRecords.CountAsync());
    }
}
