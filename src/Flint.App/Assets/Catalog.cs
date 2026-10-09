namespace Flint.App.Assets;

/// <summary>
/// <c>assets/catalog.json</c>, written by scripts/extract-catalog.py from the owner's APK: the chain's slots left to
/// right, each with every model and the model sheet's order, plus the reverb types, the knob drawables, and for each
/// pedal panel which parameter each control turns, keyed <c>id:&lt;view id&gt;</c> or <c>tag:binding_N</c>.
/// </summary>
internal sealed record Catalog(
    IReadOnlyList<ChainSlot> Chain,
    IReadOnlyList<ReverbType> ReverbTypes,
    IReadOnlyDictionary<string, string> KnobStyles,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, PanelControl>>? Panels)
{
    /// <summary>The control a panel node turns, by its id or its binding tag, or null.</summary>
    public PanelControl? Control(string layout, string? id, string? tag) =>
        Panels?.GetValueOrDefault(layout) is { } controls
            ? (id is not null ? controls.GetValueOrDefault($"id:{id}") : null) ?? (tag is not null ? controls.GetValueOrDefault($"tag:{tag}") : null)
            : null;

    /// <summary>The model with this dspId in any slot, or null for one the app does not know.</summary>
    public CatalogModel? Model(string dspId) => Chain.SelectMany(s => s.Models).FirstOrDefault(m => m.DspId == dspId);

    /// <summary>bias.reverb's type from its param 6, or null.</summary>
    public ReverbType? Reverb(float param6) => ReverbTypes.FirstOrDefault(t => MathF.Abs(t.Value - param6) < 0.05f);
}
