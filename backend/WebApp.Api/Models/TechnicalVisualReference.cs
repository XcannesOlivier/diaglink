namespace WebApp.Api.Models;

/// <summary>A validated, durable reference to a technical image stored for a machine document.</summary>
public sealed record TechnicalVisualReference(
    string DocumentId,
    int Page,
    string AssetType,
    string? Tile,
    string Name,
    string AssetKey);
