using Flint.Core.Model;
using Flint.Core.Presets;
using Flint.Core.Protocol;
using Flint.Core.Protocol.Payloads;

namespace Flint.Core.Tests;

/// <summary>
/// The amp's side of a whole session for <see cref="FakeLink.Respond"/>: the handshake reads, 8 slots it serves and
/// stores (keeping an upload as sent, as the real one does), and slot switches.
/// </summary>
public sealed class FakeAmp
{
    private readonly List<byte[]> _upload = [];

    public FakeAmp() => Slots = [.. Enumerable.Range(0, 8).Select(i => Sample($"Preset {i}"))];

    public Preset[] Slots { get; }

    /// <summary>What was uploaded to the temporary slot, 127.</summary>
    public Preset? Temporary { get; private set; }
    public Preset? Live { get; set; }
    public bool Edited { get; set; }
    public List<int> Downloaded { get; } = [];
    public List<int> Uploaded { get; } = [];
    public List<int> Selected { get; } = [];
    public SettingInfo Settings { get; set; } = new(false, 60, 0, 15);
    public bool LineOutMuted { get; set; }
    public bool TunerOn { get; set; }

    /// <summary>Every looper action we sent, in order; the amp's own reports are sent by each test.</summary>
    public List<LooperAction> Looper { get; } = [];
    public LooperAction LooperNow { get; set; } = LooperAction.ResetToReady;
    public LooperTimeSignature LooperTiming { get; set; } = new(120, 4, 4, false, true, false, 60);
    public string Name { get; set; } = "Spark 2";

    /// <summary>
    /// The captured edit buffer under a new name, with an id made from the name. Not a random id: the amp's slot checksum
    /// is an 8-bit sum, so two random presets share it once in 256, and a test replacing 8 slots then failed now and
    /// then.
    /// </summary>
    public static Preset Sample(string name) =>
        Captured with { Id = new Guid(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(name))).ToString(), Name = name };

    public static uint ChecksumOf(Preset preset) => PresetCodec.Checksum(PresetCodec.Pack(preset));

    public IEnumerable<Chunk> Respond(Chunk w) => w.Op switch
    {
        Opcode.GetSlotStates => [Reply(w, new SlotStates([new SlotState(0, 512797, Edited)]))],
        Opcode.GetSystemInfo => [Reply(w, new SystemInfo(6, 0, 1, 1, 0, 0, -99))],
        Opcode.GetFirmware or Opcode.GetName when TunerOn => [], // the real amp in tuner mode does not answer these
        Opcode.GetFirmware => [Reply(w, new UIntValue(0x020702C8))],
        Opcode.GetName => [Reply(w, new PrefixedText(Name))],
        Opcode.GetTuner => [Reply(w, new BoolValue(TunerOn))],
        Opcode.SetName => Ack(w, () => Name = PrefixedText.Decode(w.Payload).Text),
        Opcode.GetChecksums => [Reply(w, new UIntArray([.. Slots.Select(ChecksumOf)]))],
        Opcode.GetSettings => [Reply(w, Settings)],
        Opcode.SetSettings => Ack(w, () => Settings = SettingInfo.Decode(w.Payload), new UIntValue(0)),
        Opcode.SetLooperAction => Ack(w, () => Looper.Add((LooperAction)UIntValue.Decode(w.Payload).Value)),
        Opcode.GetLooperAction => [Reply(w, new UIntValue((uint)LooperNow))],
        Opcode.GetLooperTimeSignature => [Reply(w, LooperTiming)],
        Opcode.SetLooperTimeSignature => Ack(w, () => LooperTiming = LooperTimeSignature.Decode(w.Payload)),
        Opcode.SetTuner => Ack(w, () => TunerOn = BoolValue.Decode(w.Payload).Value),
        Opcode.GetLineOutMute => [Reply(w, new BoolValue(LineOutMuted))],
        Opcode.SetLineOutMute => Ack(w, () => LineOutMuted = BoolValue.Decode(w.Payload).Value),
        Opcode.DownloadPreset => Download(w, PresetRequest.Decode(w.Payload)),
        Opcode.UploadPreset => Upload(w),
        Opcode.SelectSlot => Select(w, SwitchPreset.Decode(w.Payload).Slot),
        Opcode.SetModuleOn => [new Chunk(w.Seq, 0x04, 0x15, [])],
        Opcode.ReplaceModule => Replace(w, DspReplace.Decode(w.Payload)),
        _ => [],
    };

    private IEnumerable<Chunk> Download(Chunk w, PresetRequest request)
    {
        var preset = request.Type == PresetType.Editing ? Live ?? Slots[0] : Slots[request.Slot];
        if (request.Type == PresetType.Slot) Downloaded.Add(request.Slot);
        return PresetTransfer.Split(PresetTransfer.Wrap(request.Type, request.Slot, preset), PresetTransfer.DownloadPieceSize)
            .Select(piece => new Chunk(w.Seq, 0x03, 0x01, piece));
    }

    private IEnumerable<Chunk> Upload(Chunk w)
    {
        _upload.Add(w.Payload);
        if (PresetTransfer.IsLast(w.Payload))
        {
            var (_, slot, preset) = PresetTransfer.Unwrap(PresetTransfer.Join(_upload));
            _upload.Clear();
            if (slot == 127) Temporary = preset;
            else Slots[slot] = preset;
            Uploaded.Add(slot);
        }
        return [new Chunk(w.Seq, 0x05, 0x01, [])];
    }

    /// <summary>The amp swaps the model in its live preset (keeping the knobs, which is enough for a fake).</summary>
    private IEnumerable<Chunk> Replace(Chunk w, DspReplace swap)
    {
        var live = Live ?? Slots[0];
        Live = live with { Modules = [.. live.Modules.Select(m => m.DspId == swap.From ? m with { DspId = swap.To } : m)] };
        return [new Chunk(w.Seq, 0x04, 0x06, [])];
    }

    private IEnumerable<Chunk> Select(Chunk w, int slot)
    {
        Selected.Add(slot);
        Live = slot == 127 ? Temporary : Slots[slot];
        Edited = false;
        return [new Chunk(w.Seq, 0x04, 0x38, [])];
    }

    /// <summary>A set applied and acked with <c>04 xx</c>, carrying a status where the amp sends one.</summary>
    private static IEnumerable<Chunk> Ack(Chunk w, Action apply, IPayload? status = null)
    {
        apply();
        return [new Chunk(w.Seq, 0x04, w.Sub, status?.Encode() ?? [])];
    }

    private static Chunk Reply(Chunk w, IPayload payload) => new(w.Seq, 0x03, w.Sub, payload.Encode());

    private static readonly Preset Captured = PresetTransfer.Unwrap(PresetTransfer.Join(
        [.. CaptureFile.Read("preset-download").Where(c => c.Chunk.Op == Opcode.Preset).Select(c => c.Chunk.Payload)])).Preset;
}
