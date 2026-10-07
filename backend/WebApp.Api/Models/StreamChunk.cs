namespace WebApp.Api.Models;

/// <summary>
/// Represents a chunk of streaming response data.
/// Can contain text content, annotations (citations), visuals, usage, or tool-use indicators.
/// </summary>
public record StreamChunk
{
    public AiResponseUsage? Usage { get; init; }
    public List<TechnicalVisualReference>? Visuals { get; init; }
    public List<TechnicalSourceReference>? Sources { get; init; }

    /// <summary>
    /// Text content chunk (delta). Null if this chunk contains another event type.
    /// </summary>
    public string? TextDelta { get; init; }
    
    /// <summary>
    /// Annotations/citations extracted from the response. Null if this chunk contains another event type.
    /// </summary>
    public List<AnnotationInfo>? Annotations { get; init; }
    
    /// <summary>
    /// Whether this chunk signals a tool-use step (e.g. file_search, code_interpreter).
    /// </summary>
    public bool IsToolUse { get; init; }
    
    /// <summary>
    /// Name of the tool being invoked (set when IsToolUse is true).
    /// </summary>
    public string? ToolName { get; init; }
    
    /// <summary>
    /// Creates a text delta chunk.
    /// </summary>
    public static StreamChunk Text(string delta) => new() { TextDelta = delta };
    
    /// <summary>
    /// Creates an annotations chunk.
    /// </summary>
    public static StreamChunk WithAnnotations(List<AnnotationInfo> annotations) => new() { Annotations = annotations };
    
    /// <summary>
    /// Creates a tool-use indicator chunk.
    /// </summary>
    public static StreamChunk ToolUse(string toolName) => new() { IsToolUse = true, ToolName = toolName };

    public static StreamChunk WithVisuals(IReadOnlyList<TechnicalVisualReference> visuals) =>
        new() { Visuals = visuals.ToList() };

    public static StreamChunk WithSources(IReadOnlyList<TechnicalSourceReference> sources) =>
        new() { Sources = sources.ToList() };
    
    /// <summary>
    /// Whether this chunk contains text content.
    /// </summary>
    public bool IsText => TextDelta != null;
    
    /// <summary>
    /// Whether this chunk contains annotations.
    /// </summary>
    public bool HasAnnotations => Annotations != null && Annotations.Count > 0;

    public bool HasVisuals => Visuals != null && Visuals.Count > 0;

    public bool HasSources => Sources != null && Sources.Count > 0;
}
