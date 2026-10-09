using Flint.Core.Presets;

namespace Flint.Core.Library;

/// <summary>
/// What slot sync found: the 8 presets the amp holds now, the slots that need the owner's answer, and whether those look
/// like a factory reset rather than the phone app's edits.
/// </summary>
public sealed record SyncResult(IReadOnlyList<Preset> Slots, IReadOnlyList<SlotConflict> Conflicts, bool FactoryReset);
