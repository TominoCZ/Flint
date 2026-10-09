using Flint.Core.Logging;
using Flint.Core.Model;
using Flint.Core.Presets;
using Flint.Core.Protocol.Payloads;
using Flint.Core.Session;

namespace Flint.Core.Library;

/// <summary>
/// The amp's slots against the library, at every connect. A slot whose checksum matches the slot map is read
/// from disk; only the others are downloaded. A slot with no library entry yet (the first connect) is imported. The
/// rest are conflicts, which only the owner resolves, and neither answer loses a local file (decision 9).
/// </summary>
public static class SlotSync
{
    public static async Task<SyncResult> RunAsync(AmpSession session, PresetLibrary library, IReadOnlyList<uint> checksums,
        CancellationToken ct)
    {
        var map = library.LoadSlotMap();
        var mapChanged = false;
        var slots = new Preset[AmpState.SlotCount];
        var conflicts = new List<SlotConflict>();
        for (var slot = 0; slot < AmpState.SlotCount; slot++)
        {
            var entry = map.Slots[slot];
            var mine = entry is null ? null : TryLoad(library, entry.Preset);
            if (mine is not null && entry!.Checksum == checksums[slot])
            {
                slots[slot] = mine;
                continue;
            }

            var (_, _, amps) = await session.DownloadPresetAsync(PresetType.Slot, slot, ct);
            slots[slot] = amps;
            if (mine is null)
            {
                map = map.With(slot, new SlotEntry(library.Add(amps), checksums[slot]));
                mapChanged = true;
                Log.Info("lib", $"slot {slot + 1}: imported \"{amps.Name}\" from the amp");
            }
            else
            {
                conflicts.Add(new SlotConflict(slot, entry!.Preset, mine, amps, checksums[slot]));
                Log.Info("lib", $"slot {slot + 1}: the amp has \"{amps.Name}\", the library \"{mine.Name}\"");
            }
        }
        if (mapChanged) library.SaveSlotMap(map);

        // A factory preset on any slot, or every slot changed at once.
        var factoryReset = conflicts.Count == AmpState.SlotCount || conflicts.Any(c => map.FactoryIds.Contains(c.Amps.Id));
        return new SyncResult(slots, conflicts, factoryReset);
    }

    /// <summary>"Restore mine": uploads the library's preset to its slot and returns the checksum the amp will now report.</summary>
    public static async Task<uint> RestoreAsync(AmpSession session, PresetLibrary library, SlotConflict conflict, CancellationToken ct)
    {
        var checksum = await PutAsync(session, library, conflict.Slot, conflict.Entry, conflict.Mine, ct);
        Log.Info("lib", $"slot {conflict.Slot + 1}: restored \"{conflict.Mine.Name}\"");
        return checksum;
    }

    /// <summary>
    /// Puts a library entry in a slot and returns the checksum the amp will now report. The slot map is written before the
    /// upload, so an upload cut short shows at the next connect as a slot that differs, and the owner is asked (§4.5).
    /// </summary>
    public static async Task<uint> PutAsync(AmpSession session, PresetLibrary library, int slot, string entry, Preset preset, CancellationToken ct)
    {
        var checksum = PresetTransfer.UploadChecksum(preset);
        library.SaveSlotMap(library.LoadSlotMap().With(slot, new SlotEntry(entry, checksum)));
        await session.UploadPresetAsync(PresetType.Slot, slot, preset, ct);
        return checksum;
    }

    /// <summary>"Keep the amp's": imports the amp's preset as a new entry and points the slot at it. The old file stays.</summary>
    public static void KeepAmps(PresetLibrary library, SlotConflict conflict)
    {
        var entry = library.Add(conflict.Amps);
        library.SaveSlotMap(library.LoadSlotMap().With(conflict.Slot, new SlotEntry(entry, conflict.AmpChecksum)));
        Log.Info("lib", $"slot {conflict.Slot + 1}: kept the amp's \"{conflict.Amps.Name}\" as {entry}");
    }

    /// <summary>
    /// Learns the amp's factory presets (signal 1) once the owner confirmed a factory reset by restoring. Not on detection
    /// alone: presets the owner chose to keep must not be taken for factory ones later.
    /// </summary>
    public static void RememberFactoryPresets(PresetLibrary library, IEnumerable<Preset> factory)
    {
        var map = library.LoadSlotMap();
        library.SaveSlotMap(map with { FactoryIds = [.. map.FactoryIds.Union(factory.Select(p => p.Id))] });
    }

    private static Preset? TryLoad(PresetLibrary library, string entry)
    {
        try
        {
            return library.Load(entry);
        }
        catch (Exception e) when (e is IOException or FormatException)
        {
            Log.Warn("lib", $"{entry}: {e.Message}; taking the amp's preset for its slot");
            return null;
        }
    }
}
