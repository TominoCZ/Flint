using System.Text;
using Flint.Core.Protocol;
using Flint.Core.Protocol.Payloads;
using static Flint.Core.Tests.CaptureFile;

namespace Flint.Core.Tests;

// What the official app and the owner's Spark 2 actually exchanged, one fixture per action, captured
// with scripts/spark-log.sh on 2026-10-07. These pin down the wire shapes the
// protocol layer has to produce and accept.
public class SparkTwoCaptureTests
{
    [Fact]
    public void Every_captured_chunk_decodes_with_a_valid_checksum()
    {
        foreach (var file in Directory.GetFiles("Captures", "*.txt"))
            Assert.NotEmpty(Read(Path.GetFileNameWithoutExtension(file)));
    }

    [Fact]
    public void Slot_switch_is_01_38_with_the_slot_0_to_7_and_is_acked_with_04_38()
    {
        var chunks = Read("slot-switch");
        var switches = chunks.Where(c => c.ToAmp && c.Chunk.Op == Opcode.SelectSlot).ToList();
        Assert.Equal([1, 2, 3, 0, 4, 5, 6, 7, 0], switches.Select(c => SwitchPreset.Decode(c.Chunk.Payload).Slot));
        Assert.All(switches, s => Assert.Contains(chunks, c => !c.ToAmp && c.Chunk.Op == 0x0438 && c.Chunk.Seq == s.Chunk.Seq));
    }

    [Fact]
    public void Reverb_type_is_param_6_of_bias_reverb_with_a_trailing_signal_chain_index()
    {
        var sets = Read("reverb-type").Where(c => c.ToAmp && c.Chunk.Op == Opcode.SetParameter)
            .Select(c => DspParam.Decode(c.Chunk.Payload)).ToList();
        Assert.All(sets, p => Assert.Equal(("bias.reverb", 6, 0), (p.Module, p.Param, p.SignalChain)));
        Assert.Equal([0.1f, 0.4f, 0.7f, 0.8f], sets.Select(p => MathF.Round(p.Value, 2)));
    }

    [Fact]
    public void Preset_upload_shares_one_seq_and_each_chunk_is_acked_with_05_01()
    {
        var chunks = Read("preset-upload");
        var parts = chunks.Where(c => c.ToAmp && c.Chunk.Op == Opcode.UploadPreset).Select(c => c.Chunk).ToList();
        Assert.Equal(4, parts.Count);
        Assert.Single(parts.Select(p => p.Seq).Distinct());
        Assert.Equal([0, 1, 2, 3], parts.Select(p => (int)p.Payload[1])); // <total> <index> <length>
        Assert.Equal(4, chunks.Count(c => !c.ToAmp && c.Chunk.Op == Opcode.UploadPresetAck && c.Chunk.Seq == parts[0].Seq));
    }

    [Fact]
    public void Preset_download_carries_the_full_preset_not_a_compact_one()
    {
        var parts = Read("preset-download").Where(c => !c.ToAmp && c.Chunk.Op == Opcode.Preset).Select(c => c.Chunk.Payload).ToList();
        Assert.Equal(Enumerable.Range(0, 16), parts.Select(p => (int)p[1]));
        var preset = Encoding.Latin1.GetString([.. parts.SelectMany(p => p[3..])]);
        // Name, version, icon, then the 7-module array: soundshed's "Spark 2 omits these" does not hold here.
        foreach (var expected in new[] { "Clean Rich Reverb", "0.7", "icon.png", "bias.noisegate", "GK800", "DelayRe201", "bias.reverb" })
            Assert.Contains(expected, preset);
    }

    [Fact]
    public void Tuner_is_toggled_with_01_65_and_streams_note_and_offset_on_03_64()
    {
        var chunks = Read("tuner");
        Assert.Equal([true, false], chunks.Where(c => c.ToAmp && c.Chunk.Op == Opcode.SetTuner).Select(c => BoolValue.Decode(c.Chunk.Payload).Value));
        Assert.All(chunks.Where(c => !c.ToAmp && c.Chunk.Op == Opcode.TunerReading), c => TunerReading.Decode(c.Chunk.Payload));
    }

    [Fact]
    public void Looper_reports_its_state_on_03_75_and_progress_on_03_77()
    {
        var chunks = Read("looper");
        Assert.Equal([2u, 2, 4, 7, 8, 9], chunks.Where(c => c.Chunk.Op == Opcode.LooperState).Select(c => UIntValue.Decode(c.Chunk.Payload).Value));
        var progress = chunks.Where(c => c.Chunk.Op == Opcode.LooperProgress).Select(c => FloatValue.Decode(c.Chunk.Payload).Value).ToList();
        Assert.Equal(progress.Order(), progress);
    }

    [Fact]
    public void Line_out_mute_behaviour_is_a_bool_on_01_a2_acked_with_04_a2()
    {
        var chunks = Read("line-out-mute");
        var sets = chunks.Where(c => c.ToAmp && c.Chunk.Op == Opcode.SetLineOutMute).ToList();
        Assert.Equal([false, true, true], sets.Select(c => BoolValue.Decode(c.Chunk.Payload).Value));
        Assert.All(sets, s => Assert.Contains(chunks, c => !c.ToAmp && c.Chunk.Op == 0x04A2 && c.Chunk.Seq == s.Chunk.Seq));
        Assert.Contains(chunks, c => !c.ToAmp && c.Chunk.Op == Opcode.LineOutMute); // and 02 a2 reads it
    }

    [Fact]
    public void Amp_swap_is_01_06_old_new_then_the_new_model_is_switched_on()
    {
        var chunks = Read("amp-swap").Where(c => c.ToAmp).ToList();
        var swaps = chunks.Where(c => c.Chunk.Op == Opcode.ReplaceModule).Select(c => DspReplace.Decode(c.Chunk.Payload));
        Assert.Equal([new("GK800", "Sunny3000"), new("Sunny3000", "W600"), new DspReplace("W600", "GK800")], swaps);
        foreach (var (swap, next) in chunks.Zip(chunks.Skip(1)).Where(p => p.First.Chunk.Op == Opcode.ReplaceModule))
        {
            Assert.Equal(Opcode.SetModuleOn, next.Chunk.Op);
            Assert.Equal(new DspOnOff(DspReplace.Decode(swap.Chunk.Payload).To, true), DspOnOff.Decode(next.Chunk.Payload));
        }
    }

    [Fact]
    public void Amp_knobs_gain_bass_middle_treble_volume_are_params_0_3_2_1_4()
    {
        // The owner turned Gain, Bass, Middle, Treble, Volume, in that order, on the GK800.
        var parameters = Read("amp-knobs").Where(c => c.ToAmp && c.Chunk.Op == Opcode.SetParameter)
            .Select(c => DspParam.Decode(c.Chunk.Payload)).Select(p => (p.Module, p.Param));
        Assert.Equal([("GK800", 0), ("GK800", 3), ("GK800", 2), ("GK800", 1), ("GK800", 4)], parameters.Distinct());
    }

    [Fact]
    public void The_amps_preset_selector_is_reported_as_03_38_with_the_slot()
    {
        // A sweep through bank A (1→3→0), over to B (7→4) and back.
        var slots = Read("amp-preset-selector").Where(c => !c.ToAmp && c.Chunk.Op == Opcode.AmpSlot)
            .Select(c => SwitchPreset.Decode(c.Chunk.Payload).Slot);
        Assert.Equal([1, 2, 3, 2, 1, 0, 7, 6, 5, 4, 5, 6, 7, 0], slots);
    }

    [Fact]
    public void The_amps_own_knobs_are_reported_as_03_37_and_fx_knobs_also_toggle_their_pedal()
    {
        // Turned on the amp in this order: GAIN, BASS, MID, TREBLE, MASTER, MOD, DELAY, REVERB. MUSIC and GUITAR
        // are analog and send nothing.
        var chunks = Read("amp-physical-knobs").Where(c => !c.ToAmp).ToList();
        var knobs = chunks.Where(c => c.Chunk.Op == Opcode.AmpParameter)
            .Select(c => DspParam.Decode(c.Chunk.Payload)).Select(p => (p.Module, p.Param));
        Assert.Equal([("GK800", 0), ("GK800", 3), ("GK800", 2), ("GK800", 1), ("GK800", 4),
                      ("GuitarEQ6", 4), ("DelayRe201", 1), ("bias.reverb", 0)], knobs.Distinct());
        // An FX knob turned to zero switches its pedal off (03 15 … C2), and above zero back on.
        var toggles = chunks.Where(c => c.Chunk.Op == Opcode.ModuleOn).Select(c => DspOnOff.Decode(c.Chunk.Payload)).ToList();
        Assert.Contains(new DspOnOff("GuitarEQ6", false), toggles);
        Assert.Contains(new DspOnOff("DelayRe201", false), toggles);
    }
}
