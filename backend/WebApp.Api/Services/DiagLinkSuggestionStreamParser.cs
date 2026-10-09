using System.Text;

namespace WebApp.Api.Services;

/// <summary>
/// Incrementally removes DiagLink's internal suggestion protocol from visible text.
/// The parser deliberately keeps only the smallest possible opening-tag prefix while
/// in text mode, so ordinary answer text can continue to stream immediately.
/// </summary>
internal sealed class DiagLinkSuggestionStreamParser
{
    internal const string OpeningTag = "<diaglink_suggestion>";
    internal const string ClosingTag = "</diaglink_suggestion>";
    internal const int MaximumSuggestions = 2;
    internal const int MaximumSuggestionLength = 240;

    private readonly StringBuilder _buffer = new();
    private readonly List<string> _suggestions = [];
    private readonly HashSet<string> _deduplication = new(StringComparer.OrdinalIgnoreCase);
    private bool _insideSuggestion;
    private bool _invalidSuggestion;
    private bool _completed;

    internal IReadOnlyList<string> Suggestions => _suggestions;

    internal IReadOnlyList<string> Push(string? delta)
    {
        if (_completed || string.IsNullOrEmpty(delta))
        {
            return [];
        }

        _buffer.Append(delta);
        return Drain(final: false);
    }

    internal IReadOnlyList<string> Complete()
    {
        if (_completed)
        {
            return [];
        }

        _completed = true;
        return Drain(final: true);
    }

    private IReadOnlyList<string> Drain(bool final)
    {
        var visible = new List<string>();

        while (_buffer.Length > 0)
        {
            if (_insideSuggestion)
            {
                if (!DrainSuggestion(visible, final))
                {
                    break;
                }

                continue;
            }

            if (!DrainText(visible, final))
            {
                break;
            }
        }

        return visible;
    }

    private bool DrainText(List<string> visible, bool final)
    {
        var value = _buffer.ToString();
        var markerStart = value.IndexOf('<');
        if (markerStart < 0)
        {
            Emit(visible, value);
            _buffer.Clear();
            return false;
        }

        if (markerStart > 0)
        {
            Emit(visible, value[..markerStart]);
            _buffer.Remove(0, markerStart);
            return true;
        }

        value = _buffer.ToString();
        if (value.StartsWith(OpeningTag, StringComparison.Ordinal))
        {
            _buffer.Remove(0, OpeningTag.Length);
            _insideSuggestion = true;
            _invalidSuggestion = false;
            return true;
        }

        if (!final &&
            (OpeningTag.StartsWith(value, StringComparison.Ordinal) ||
             ClosingTag.StartsWith(value, StringComparison.Ordinal)))
        {
            return false;
        }

        if (LooksLikeReservedMarker(value))
        {
            var markerEnd = value.IndexOf('>');
            if (markerEnd < 0)
            {
                if (!final)
                {
                    return false;
                }

                _buffer.Clear();
                return false;
            }

            // Suppress malformed internal markers without exposing them to Markdown.
            _buffer.Remove(0, markerEnd + 1);
            return true;
        }

        Emit(visible, "<");
        _buffer.Remove(0, 1);
        return true;
    }

    private bool DrainSuggestion(List<string> visible, bool final)
    {
        var value = _buffer.ToString();
        var nestedStart = value.IndexOf(OpeningTag, StringComparison.Ordinal);
        var closingStart = value.IndexOf(ClosingTag, StringComparison.Ordinal);

        if (nestedStart >= 0 && (closingStart < 0 || nestedStart < closingStart))
        {
            _invalidSuggestion = true;
            _buffer.Remove(nestedStart, OpeningTag.Length);
            return true;
        }

        if (closingStart < 0)
        {
            if (!final)
            {
                return false;
            }

            // An unclosed block is not a suggestion. Preserve its text, but never its marker.
            Emit(visible, RemoveReservedMarkers(value));
            _buffer.Clear();
            _insideSuggestion = false;
            return false;
        }

        var candidate = value[..closingStart];
        _buffer.Remove(0, closingStart + ClosingTag.Length);
        _insideSuggestion = false;

        if (_invalidSuggestion)
        {
            Emit(visible, RemoveReservedMarkers(candidate));
        }
        else
        {
            AddSuggestion(candidate);
        }

        _invalidSuggestion = false;
        return true;
    }

    private void AddSuggestion(string candidate)
    {
        var normalized = string.Join(
            " ",
            candidate.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        if (normalized.Length is 0 or > MaximumSuggestionLength ||
            _suggestions.Count >= MaximumSuggestions ||
            !_deduplication.Add(normalized))
        {
            return;
        }

        _suggestions.Add(normalized);
    }

    private static bool LooksLikeReservedMarker(string value) =>
        value.StartsWith("<diaglink_suggestion", StringComparison.Ordinal) ||
        value.StartsWith("</diaglink_suggestion", StringComparison.Ordinal);

    private static string RemoveReservedMarkers(string value)
    {
        var result = new StringBuilder(value.Length);
        var offset = 0;
        while (offset < value.Length)
        {
            var marker = value.IndexOf("<diaglink_suggestion", offset, StringComparison.Ordinal);
            var closingMarker = value.IndexOf("</diaglink_suggestion", offset, StringComparison.Ordinal);
            if (marker < 0 || (closingMarker >= 0 && closingMarker < marker))
            {
                marker = closingMarker;
            }

            if (marker < 0)
            {
                result.Append(value.AsSpan(offset));
                break;
            }

            result.Append(value.AsSpan(offset, marker - offset));
            var markerEnd = value.IndexOf('>', marker);
            if (markerEnd < 0)
            {
                break;
            }

            offset = markerEnd + 1;
        }

        return result.ToString();
    }

    private static void Emit(List<string> visible, string value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            visible.Add(value);
        }
    }
}
