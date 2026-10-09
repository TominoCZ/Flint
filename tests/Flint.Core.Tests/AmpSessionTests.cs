using Flint.Core.Presets;
using Flint.Core.Protocol;
using Flint.Core.Protocol.Payloads;
using Flint.Core.Session;

namespace Flint.Core.Tests;

// The session against the amp's side of real captures (CaptureReplay): what we write must be what the official app
// wrote, and the amp's replies must come back to the request that asked.
public sealed class AmpSessionTests : IDisposable
{
    private readonly FakeLink _link = new();
    private readonly AmpSession _session;
    private readonly List<Chunk> _notified = [];

    public AmpSessionTests()
    {
        _link.ConnectAsync(CancellationToken.None).Wait();
        _session = new AmpSession(_link, TimeSpan.FromMilliseconds(200));
        _session.Notified += (chunk, _) =>
        {
            lock (_notified) _notified.Add(chunk);
        };
    }

    public void Dispose() => _session.Dispose();

    [Fact]
    public async Task Gets_return_the_reply_to_their_own_request()
    {
        var replay = Replay("device-info");

        Assert.Equal("Spark 2", (await _session.GetAsync<PrefixedText>(Opcode.GetName)).Text);
        Assert.Equal(0x020702C8u, (await _session.GetAsync<UIntValue>(Opcode.GetFirmware)).Value);
        Assert.False((await _session.GetAsync<BoolValue>(Opcode.GetControlPedalConnected)).Value);
        Assert.Equal(0, replay.Remaining);
    }

    [Fact]
    public async Task A_slot_switch_is_acked_and_the_amps_03_76_after_it_is_a_notify()
    {
        var replay = Replay("slot-switch");

        foreach (var slot in new[] { 1, 2, 3, 0, 4, 5, 6, 7, 0 }) // as the owner pressed them
            Assert.Equal(0x0438, (await _session.RequestAsync(Opcode.SelectSlot, new SwitchPreset(PresetType.Slot, slot))).Op);
        Assert.Equal(0, replay.Remaining);

        Assert.Equal(9, _notified.Count);
        Assert.All(_notified, c => Assert.Equal(Opcode.LooperTimeSignature, c.Op));
    }

    [Fact]
    public async Task Downloads_collect_every_03_01_piece_and_unwrap_the_preset()
    {
        var replay = Replay("slot-checksums");

        var checksums = (await _session.GetAsync<UIntArray>(Opcode.GetChecksums, new UIntValue(0))).Values;
        foreach (var slot in new[] { 1, 2, 5, 7 })
        {
            var (type, got, preset) = await _session.DownloadPresetAsync(PresetType.Slot, slot);
            Assert.Equal((PresetType.Slot, slot), (type, got));
            Assert.Equal(checksums[slot], PresetCodec.Checksum(PresetCodec.Pack(preset)));
        }
        Assert.Equal(0, replay.Remaining);
    }

    [Fact]
    public async Task An_upload_sends_the_apps_exact_pieces_under_one_seq_and_waits_for_each_05_01()
    {
        var captured = CaptureFile.Read("preset-upload").Where(c => c.ToAmp && c.Chunk.Op == Opcode.UploadPreset).ToList();
        var (type, slot, preset) = PresetTransfer.Unwrap(PresetTransfer.Join([.. captured.Select(c => c.Chunk.Payload)]));
        var replay = Replay("preset-upload");

        await _session.UploadPresetAsync(type, slot, preset);
        await _session.RequestAsync(Opcode.SelectSlot, new SwitchPreset(PresetType.Slot, 0)); // what the app does next

        var pieces = _link.Written.Where(c => c.Op == Opcode.UploadPreset).ToList();
        Assert.Equal(4, pieces.Count);
        Assert.Single(pieces.Select(c => c.Seq).Distinct());
        Assert.Equal([Opcode.SlotStates, Opcode.LooperTimeSignature], _notified.Select(c => c.Op));
        Assert.Equal(0, replay.Remaining);
    }

    [Fact]
    public async Task A_preset_from_the_amp_is_uploaded_with_the_apps_two_floats()
    {
        var downloaded = PresetTransfer.Unwrap(PresetTransfer.Join(
            [.. CaptureFile.Read("preset-download").Where(c => c.Chunk.Op == Opcode.Preset).Select(c => c.Chunk.Payload)])).Preset;
        Assert.Null(downloaded.Loudness);
        _link.Respond = w => [new Chunk(w.Seq, 0x05, 0x01, [])];

        await _session.UploadPresetAsync(PresetType.Slot, 3, downloaded);

        var sent = PresetTransfer.Unwrap(PresetTransfer.Join([.. _link.Written.Select(c => c.Payload)]));
        Assert.Equal((PresetType.Slot, 3), (sent.Type, sent.Slot));
        Assert.Equal((Preset.UnsetLoudness, Preset.DefaultExtraGain), (sent.Preset.Loudness, sent.Preset.ExtraGain));
    }

    [Fact]
    public async Task An_amp_notify_with_the_same_opcode_as_the_pending_reply_is_not_taken_for_it()
    {
        // While 02 1a waits, the amp reports a knob edit with its own 03 1a (seq 0x4e, from the amp's range).
        var reply = new Chunk(0, 0x03, 0x1A, [0x91, 0x00, 0x01, 0xC2]);
        _link.Respond = written =>
        [
            new Chunk(0x4E, 0x03, 0x1A, [0x91, 0x00, 0x00, 0xC3]),
            reply with { Seq = written.Seq },
        ];

        var states = await _session.GetAsync<SlotStates>(Opcode.GetSlotStates, new UIntArray([0]));

        Assert.Equal(Convert.ToHexString(reply.Payload), Convert.ToHexString(states.Encode()));
        Assert.Equal(0x4E, Assert.Single(_notified).Seq);
    }

    [Fact]
    public async Task A_request_with_no_reply_times_out_and_the_session_carries_on()
    {
        await Assert.ThrowsAsync<TimeoutException>(() => _session.RequestAsync(Opcode.GetInOutLink, new UIntValue(14)));

        _link.Respond = w => [new Chunk(w.Seq, 0x03, 0x11, new PrefixedText("Spark 2").Encode())];
        Assert.Equal("Spark 2", (await _session.GetAsync<PrefixedText>(Opcode.GetName)).Text);
    }

    [Fact]
    public async Task A_drop_fails_the_pending_request_at_once_and_every_later_one()
    {
        var pending = _session.RequestAsync(Opcode.GetName);
        _link.Drop("test");

        Assert.Contains("test", (await Assert.ThrowsAsync<IOException>(() => pending.WaitAsync(TimeSpan.FromMilliseconds(100)))).Message);
        await Assert.ThrowsAsync<IOException>(() => _session.RequestAsync(Opcode.GetName));
    }

    [Fact]
    public async Task Seqs_count_from_01_to_3f_and_wrap_to_01()
    {
        _link.Respond = w => [new Chunk(w.Seq, 0x03, w.Sub, [])];
        for (var i = 0; i < 0x40; i++) await _session.RequestAsync(Opcode.GetSettings);

        Assert.Equal([.. Enumerable.Range(1, 0x3F), 1], _link.Written.Select(c => (int)c.Seq));
    }

    [Fact]
    public async Task A_knob_drag_sends_only_the_latest_value_per_knob()
    {
        for (var i = 1; i <= 100; i++)
        {
            _session.SetParameter(new DspParam("GK800", 0, i / 100f));
            _session.SetParameter(new DspParam("GK800", 4, 1 - i / 100f));
        }
        await Task.Delay(AmpSession.KnobInterval * 4);

        var sent = _link.Written.Select(c => DspParam.Decode(c.Payload)).ToList();
        Assert.All(_link.Written, c => Assert.Equal(Opcode.SetParameter, c.Op));
        Assert.InRange(sent.Count, 2, 4); // the first value of each, then the last of each
        Assert.Equal(1f, sent.Last(p => p.Param == 0).Value);
        Assert.Equal(0f, sent.Last(p => p.Param == 4).Value);
    }

    private CaptureReplay Replay(string name)
    {
        var replay = new CaptureReplay(name);
        _link.Respond = replay.Respond;
        return replay;
    }
}
