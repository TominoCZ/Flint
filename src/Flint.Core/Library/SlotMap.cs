using Flint.Core.Model;

namespace Flint.Core.Library;

/// <summary>
/// <c>slots.json</c>: which library entry belongs in each of the 8 hardware slots (null: none yet), and the
/// preset ids known to be the amp's factory presets, learned from the first factory reset the owner restored.
/// </summary>
public sealed record SlotMap(IReadOnlyList<SlotEntry?> Slots, IReadOnlyList<string> FactoryIds)
{
    public static SlotMap Empty => new(new SlotEntry?[AmpState.SlotCount], []);

    public SlotMap With(int slot, SlotEntry entry) => this with { Slots = [.. Slots.Select((e, i) => i == slot ? entry : e)] };

    /// <summary>Exactly 8 slots, whatever a hand-edited file held.</summary>
    public SlotMap Normalised() => this with
    {
        Slots = [.. Enumerable.Range(0, AmpState.SlotCount).Select(i => i < (Slots?.Count ?? 0) ? Slots![i] : null)],
        FactoryIds = FactoryIds ?? [],
    };
}
