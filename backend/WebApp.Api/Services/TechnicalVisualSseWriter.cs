using System.Text.Json;
using WebApp.Api.Models;

namespace WebApp.Api.Services;

internal static class TechnicalVisualSseWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static async Task WriteBeforeDoneAsync(
        HttpResponse response,
        IReadOnlyList<ConversationMessageVisualInfo> visuals,
        CancellationToken cancellationToken,
        Func<HttpResponse, CancellationToken, Task> writeDone) =>
        await WriteBeforeDoneAsync(response, visuals, [], cancellationToken, writeDone);

    internal static async Task WriteBeforeDoneAsync(
        HttpResponse response,
        IReadOnlyList<ConversationMessageVisualInfo> visuals,
        IReadOnlyList<ConversationMessageSourceReferenceInfo> sources,
        CancellationToken cancellationToken,
        Func<HttpResponse, CancellationToken, Task> writeDone)
    {
        if (visuals.Count > 0)
        {
            var json = JsonSerializer.Serialize(new { type = "visuals", visuals }, JsonOptions);
            await response.WriteAsync($"data: {json}\n\n", cancellationToken);
            await response.Body.FlushAsync(cancellationToken);
        }

        if (sources.Count > 0)
        {
            var json = JsonSerializer.Serialize(new { type = "sources", sources }, JsonOptions);
            await response.WriteAsync($"data: {json}\n\n", cancellationToken);
            await response.Body.FlushAsync(cancellationToken);
        }

        await writeDone(response, cancellationToken);
    }
}
