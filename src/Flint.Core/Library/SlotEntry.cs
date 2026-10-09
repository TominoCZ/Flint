namespace Flint.Core.Library;

/// <summary>One hardware slot in the slot map: the library entry it holds, and that preset's checksum as the amp reports it.</summary>
public sealed record SlotEntry(string Preset, uint Checksum);
