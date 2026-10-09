using Flint.Core.Logging;
using Flint.Core.Protocol;
using Flint.Core.Protocol.Payloads;
using Flint.Core.Session;

namespace Flint.Core.Model;

/// <summary>
/// The official app's connect reads, in its order, minus the serial and the paid-pack
/// unlock. Slots are left to slot sync, which downloads only those it has to.
/// </summary>
public static class Handshake
{
    public static async Task<AmpState> ReadAsync(AmpSession session, CancellationToken ct)
    {
        // In tuner mode the amp drops replies at random, so a tuner left on (an app killed while it was open, or the amp's
        // own button) failed every connect. It goes off while the amp is read; the controller turns it on again after,
        // so the window opens the tuner and the owner closes it there (AmpState.TunerOn says it was on).
        var tuner = (await session.GetAsync<BoolValue>(Opcode.GetTuner, null, ct)).Value;
        if (tuner)
        {
            Log.Info("amp", "the amp is in tuner mode: the tuner goes off while the amp is read, then on again");
            await session.RequestAsync(Opcode.SetTuner, new BoolValue(false), ct);
        }
        var states = await session.GetAsync<SlotStates>(Opcode.GetSlotStates, new UIntArray([0]), ct);
        var info = await session.GetAsync<SystemInfo>(Opcode.GetSystemInfo, null, ct);
        var (_, _, live) = await session.DownloadPresetAsync(PresetType.Editing, 0, ct);
        var firmware = (await session.GetAsync<UIntValue>(Opcode.GetFirmware, null, ct)).Value;
        var name = (await session.GetAsync<PrefixedText>(Opcode.GetName, null, ct)).Text;
        var checksums = (await session.GetAsync<UIntArray>(Opcode.GetChecksums, new UIntValue(0), ct)).Values;
        var settings = await session.GetAsync<SettingInfo>(Opcode.GetSettings, null, ct);
        var lineOutMuted = (await session.GetAsync<BoolValue>(Opcode.GetLineOutMute, null, ct)).Value;
        if (checksums.Count != AmpState.SlotCount)
            throw new FormatException($"02 2b gave {checksums.Count} checksums, not {AmpState.SlotCount}");

        return new AmpState(name, FormatFirmware(firmware), info, settings, checksums, live,
            states.States.Count > 0 && states.States[0].Edited, null, []) { LineOutMuted = lineOutMuted, TunerOn = tuner };
    }

    /// <summary>One byte per part, most significant first: 0x020702C8 is 2.7.2.200.</summary>
    public static string FormatFirmware(uint value) => $"{value >> 24}.{(value >> 16) & 0xFF}.{(value >> 8) & 0xFF}.{value & 0xFF}";
}
