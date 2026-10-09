using Flint.Core.Presets;
using Flint.Core.Protocol;
using Flint.Core.Protocol.Payloads;

namespace Flint.Core.Model;

/// <summary>
/// What the window shows of the amp, as one immutable snapshot: <see cref="Handshake"/> reads it at
/// connect, slot sync fills <see cref="Slots"/>, and <see cref="Apply"/> keeps it current from the amp's notifies.
/// </summary>
/// <param name="Checksums">The amp's <c>02 2b</c> value per slot, 0–7.</param>
/// <param name="Live">The edit buffer: what the amp plays right now, unsaved edits included.</param>
/// <param name="Edited">The live preset differs from its slot.</param>
/// <param name="CurrentSlot">The slot the live preset came from, or null while unknown (see <see cref="WithSlots"/>).</param>
/// <param name="Slots">The 8 stored presets once slot sync has run, empty before.</param>
public sealed record AmpState(
    string Name,
    string Firmware,
    SystemInfo SystemInfo,
    SettingInfo Settings,
    IReadOnlyList<uint> Checksums,
    Preset Live,
    bool Edited,
    int? CurrentSlot,
    IReadOnlyList<Preset> Slots)
{
    public const int SlotCount = 8;

    /// <summary>The device page's Line Out Mute Behavior (<c>02 a2</c>): with a line-out plug in, the speaker is muted.</summary>
    public bool LineOutMuted { get; init; }

    /// <summary>The amp's tuner is on: switched by us (<c>01 65</c>) or by the amp's own button (<c>03 65</c>).</summary>
    public bool TunerOn { get; init; }

    /// <summary>The tuner's last reading (<c>03 64</c>), null while it is off or has not heard a string yet.</summary>
    public TunerReading? Tuning { get; init; }

    /// <summary>The amp's looper as the Simple Looper shows it.</summary>
    public Looper Looper { get; init; } = Looper.Initial;

    /// <summary>The amp's scratch slot: the official app loads a preset that is not in a slot by uploading it here and switching to it.</summary>
    public const int TemporarySlot = 127;

    /// <summary>
    /// The state with the slots filled in. <c>02 1a</c> does not say which slot is current (its slot field counts time
    /// while unedited), so it is the one slot holding the live preset's id, if exactly one does.
    /// </summary>
    public AmpState WithSlots(IReadOnlyList<Preset> slots)
    {
        var matches = Enumerable.Range(0, slots.Count).Where(i => slots[i].Id == Live.Id).ToList();
        return this with { Slots = slots, CurrentSlot = matches.Count == 1 ? matches[0] : null };
    }

    /// <summary>
    /// The state after the amp selected a slot, by its own selector or acking our <c>01 38</c>. The temporary slot is not a
    /// stored one: its preset is whatever was uploaded there, so the live preset stays.
    /// </summary>
    public AmpState WithCurrentSlot(int slot) => slot is >= 0 and < SlotCount
        ? this with { CurrentSlot = slot, Edited = false, Live = slot < Slots.Count ? Slots[slot] : Live }
        : this with { CurrentSlot = null, Edited = false };

    /// <summary>The tuner switched on or off; a fresh start has no reading yet.</summary>
    public AmpState WithTuner(bool on) => this with { TunerOn = on, Tuning = null };

    /// <summary>The state after one of the amp's own notifies; anything else leaves it as it is.</summary>
    public AmpState Apply(Chunk chunk, IPayload? payload) => (chunk.Op, payload) switch
    {
        (Opcode.AmpParameter, DspParam p) => this with { Live = Live.WithParameter(p.Module, p.Param, p.Value), Edited = true },
        (Opcode.ModuleOn, DspOnOff o) => this with { Live = Live.WithModuleOn(o.Module, o.On), Edited = true },
        (Opcode.AmpSlot, SwitchPreset s) when s.Type == PresetType.Slot => WithCurrentSlot(s.Slot),
        (Opcode.SlotStates, SlotStates { States.Count: > 0 } s) => this with { Edited = s.States[0].Edited },
        (Opcode.SystemInfo, SystemInfo info) => this with { SystemInfo = info },
        (Opcode.Tuner, BoolValue on) => WithTuner(on.Value),
        (Opcode.TunerReading, TunerReading reading) when TunerOn => this with { Tuning = reading },
        (Opcode.LooperState, UIntValue action) => this with { Looper = Looper.Reported((LooperAction)action.Value) },
        (Opcode.LooperProgress, FloatValue progress) when Looper.Phase is LooperPhase.Playing or LooperPhase.Dubbing
            => this with { Looper = Looper with { Progress = progress.Value } },
        (Opcode.LooperTimeSignature, LooperTimeSignature timing) => this with { Looper = Looper with { Timing = timing } },
        (Opcode.Settings, SettingInfo settings) => this with { Settings = settings },
        (Opcode.LineOutMute, BoolValue muted) => this with { LineOutMuted = muted.Value },
        _ => this,
    };
}
