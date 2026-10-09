using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WebApp.Api.Services;

/// <summary>Rebuilds the canonical Messages response from Anthropic SSE events.</summary>
internal sealed class AnthropicMessageStreamAccumulator
{
    private readonly SortedDictionary<int, ContentBlockAccumulator> _blocks = [];
    private readonly JsonObject _message = new()
    {
        ["type"] = "message",
        ["role"] = "assistant"
    };
    private JsonObject _usage = [];
    private bool _started;
    private bool _stopped;

    internal string? Apply(JsonElement streamEvent)
    {
        var type = ReadString(streamEvent, "type");
        switch (type)
        {
            case "message_start":
                ApplyMessageStart(streamEvent);
                break;
            case "content_block_start":
                return ApplyContentBlockStart(streamEvent);
            case "content_block_delta":
                return ApplyContentBlockDelta(streamEvent);
            case "content_block_stop":
            case "ping":
                break;
            case "message_delta":
                ApplyMessageDelta(streamEvent);
                break;
            case "message_stop":
                _stopped = true;
                break;
            case "error":
                throw new InvalidDataException(ReadStreamError(streamEvent));
            default:
                // Anthropic may add event types; unknown events must not break the stream.
                break;
        }

        return null;
    }

    internal JsonDocument BuildDocument()
    {
        if (!_started || !_stopped)
        {
            throw new InvalidDataException("The Claude Messages stream ended before a complete message was received.");
        }

        _message["content"] = new JsonArray(_blocks.Values.Select(block => block.Build()).ToArray());
        _message["usage"] = _usage.DeepClone();
        return JsonDocument.Parse(_message.ToJsonString());
    }

    private void ApplyMessageStart(JsonElement streamEvent)
    {
        if (!streamEvent.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("The Claude Messages stream has an invalid message_start event.");
        }

        _started = true;
        CopyIfPresent(message, _message, "id");
        CopyIfPresent(message, _message, "type");
        CopyIfPresent(message, _message, "role");
        CopyIfPresent(message, _message, "model");
        CopyIfPresent(message, _message, "stop_reason");
        CopyIfPresent(message, _message, "stop_sequence");
        if (message.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
        {
            _usage = CloneObject(usage);
        }
    }

    private string? ApplyContentBlockStart(JsonElement streamEvent)
    {
        if (!TryReadIndex(streamEvent, out var index) ||
            !streamEvent.TryGetProperty("content_block", out var block) ||
            block.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("The Claude Messages stream has an invalid content_block_start event.");
        }

        _blocks[index] = new ContentBlockAccumulator(block);
        return ReadString(block, "type") == "text"
            ? ReadString(block, "text")
            : null;
    }

    private string? ApplyContentBlockDelta(JsonElement streamEvent)
    {
        if (!TryReadIndex(streamEvent, out var index) ||
            !_blocks.TryGetValue(index, out var block) ||
            !streamEvent.TryGetProperty("delta", out var delta) ||
            delta.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("The Claude Messages stream has an invalid content_block_delta event.");
        }

        return block.Apply(delta);
    }

    private void ApplyMessageDelta(JsonElement streamEvent)
    {
        if (streamEvent.TryGetProperty("delta", out var delta) && delta.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in delta.EnumerateObject())
            {
                _message[property.Name] = Clone(property.Value);
            }
        }

        if (streamEvent.TryGetProperty("usage", out var usage) &&
            usage.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in usage.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Object &&
                    _usage[property.Name] is JsonObject existingObject)
                {
                    foreach (var nestedProperty in property.Value.EnumerateObject())
                    {
                        existingObject[nestedProperty.Name] = Clone(nestedProperty.Value);
                    }
                }
                else
                {
                    _usage[property.Name] = Clone(property.Value);
                }
            }
        }
    }


    private static string ReadStreamError(JsonElement streamEvent)
    {
        if (streamEvent.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
        {
            return ReadString(error, "message") ?? "The Claude Messages stream returned an error.";
        }

        return "The Claude Messages stream returned an error.";
    }

    private sealed class ContentBlockAccumulator
    {
        private readonly JsonObject _block;
        private readonly StringBuilder _inputJson = new();

        internal ContentBlockAccumulator(JsonElement block) => _block = CloneObject(block);

        internal string? Apply(JsonElement delta)
        {
            switch (ReadString(delta, "type"))
            {
                case "text_delta":
                    var text = ReadString(delta, "text") ?? string.Empty;
                    Append("text", text);
                    return text;
                case "input_json_delta":
                    _inputJson.Append(ReadString(delta, "partial_json"));
                    break;
                case "thinking_delta":
                    Append("thinking", ReadString(delta, "thinking") ?? string.Empty);
                    break;
                case "signature_delta":
                    _block["signature"] = ReadString(delta, "signature") ?? string.Empty;
                    break;
                case "citations_delta":
                    if (delta.TryGetProperty("citation", out var citation))
                    {
                        var citations = _block["citations"] as JsonArray ?? [];
                        citations.Add(Clone(citation));
                        _block["citations"] = citations;
                    }
                    break;
            }

            return null;
        }

        internal JsonNode Build()
        {
            if (_inputJson.Length > 0)
            {
                try
                {
                    _block["input"] = JsonNode.Parse(_inputJson.ToString());
                }
                catch (JsonException exception)
                {
                    throw new InvalidDataException("A streamed Claude tool input is not valid JSON.", exception);
                }
            }

            return _block.DeepClone();
        }

        private void Append(string propertyName, string value) =>
            _block[propertyName] = (_block[propertyName]?.GetValue<string>() ?? string.Empty) + value;
    }

    private static bool TryReadIndex(JsonElement element, out int index)
    {
        index = -1;
        return element.TryGetProperty("index", out var property) &&
            property.TryGetInt32(out index) &&
            index >= 0;
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static void CopyIfPresent(JsonElement source, JsonObject destination, string propertyName)
    {
        if (source.TryGetProperty(propertyName, out var value))
        {
            destination[propertyName] = Clone(value);
        }
    }

    private static JsonObject CloneObject(JsonElement element) =>
        JsonNode.Parse(element.GetRawText())?.AsObject() ?? [];

    private static JsonNode? Clone(JsonElement element) => JsonNode.Parse(element.GetRawText());
}
