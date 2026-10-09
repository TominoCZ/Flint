using Flint.Core.Presets;

namespace Flint.Core.Library;

/// <summary>A slot whose preset on the amp is not the one the slot map gave it: the owner decides which wins (decision 9).</summary>
public sealed record SlotConflict(int Slot, string Entry, Preset Mine, Preset Amps, uint AmpChecksum);
