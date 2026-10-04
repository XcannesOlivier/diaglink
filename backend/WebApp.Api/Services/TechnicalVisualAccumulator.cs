using WebApp.Api.Models;

namespace WebApp.Api.Services;

internal sealed class TechnicalVisualAccumulator
{
    private readonly HashSet<string> assetKeys = new(StringComparer.Ordinal);
    private readonly List<TechnicalVisualReference> items = [];

    public IReadOnlyList<TechnicalVisualReference> Items => items;

    public void AddRange(IEnumerable<TechnicalVisualReference> visuals)
    {
        foreach (var visual in visuals)
        {
            if (assetKeys.Add(visual.AssetKey))
                items.Add(visual);
        }
    }
}
