using Flint.Core.Model;
using Flint.Core.Protocol;
using Flint.Core.Protocol.Payloads;
using Flint.Core.Session;

namespace Flint.Core.Tests;

// The connect reads into an AmpState, and the amp's own notifies from real captures keeping it current.
public sealed class AmpStateTests
{
    private readonly FakeAmp _amp = new();

    [Fact]
    public async Task The_handshake_reads_the_amp_in_the_official_apps_order()
    {
        var link = new FakeLink { Respond = _amp.Respond };
        await link.ConnectAsync(CancellationToken.None);
        using var session = new AmpSession(link);
        _amp.Live = _amp.Slots[3];

        var state = await Handshake.ReadAsync(session, CancellationToken.None);

        Assert.Equal([Opcode.GetTuner, Opcode.GetSlotStates, Opcode.GetSystemInfo, Opcode.DownloadPreset, Opcode.GetFirmware, Opcode.GetName,
            Opcode.GetChecksums, Opcode.GetSettings, Opcode.GetLineOutMute], link.Written.Select(c => c.Op));
        Assert.Equal(("Spark 2", "2.7.2.200", "Preset 3"), (state.Name, state.Firmware, state.Live.Name));
        Assert.Equal(_amp.Slots.Select(FakeAmp.ChecksumOf), state.Checksums);
        Assert.Null(state.CurrentSlot);
        Assert.Equal(3, state.WithSlots(_amp.Slots).CurrentSlot); // found by the live preset's id
    }

    [Fact]
    public void The_amps_own_knobs_update_the_live_preset_and_mark_it_edited()
    {
        var notifies = CaptureFile.Read("amp-physical-knobs").Where(c => !c.ToAmp).Select(c => c.Chunk).ToList();
        var last = notifies.Where(c => c.Op == Opcode.AmpParameter).Select(c => DspParam.Decode(c.Payload))
            .Where(p => p.Module == "GK800").GroupBy(p => p.Param).ToDictionary(g => g.Key, g => g.Last().Value);

        var state = notifies.Aggregate(Unedited(), (s, c) => s.Apply(c, PayloadDecoder.Decode(c)));

        Assert.NotEmpty(last);
        Assert.True(state.Edited);
        var amp = state.Live.Modules.Single(m => m.DspId == "GK800");
        Assert.All(last, knob => Assert.Equal(knob.Value, amp.Parameters.Single(p => p.Index == knob.Key).Values[0]));
    }

    [Fact]
    public void The_amps_preset_selector_makes_that_slot_current_and_unedited()
    {
        var state = Unedited() with { Edited = true };
        foreach (var c in CaptureFile.Read("amp-preset-selector").Where(c => !c.ToAmp && c.Chunk.Op == Opcode.AmpSlot))
            state = state.Apply(c.Chunk, PayloadDecoder.Decode(c.Chunk));

        Assert.Equal(0, state.CurrentSlot); // the owner turned it 1, 2, 3, 2, 1, 0
        Assert.False(state.Edited);
        Assert.Same(_amp.Slots[0], state.Live);
    }

    private AmpState Unedited() => new AmpState("Spark 2", "2.7.2.200", new SystemInfo(6, 0, 1, 1, 0, 0, -99),
        new SettingInfo(false, 60, 0, 15), [], _amp.Slots[5], false, null, []).WithSlots(_amp.Slots);
}
