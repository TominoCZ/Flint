using Flint.Core.Presets;
using Flint.Core.Protocol;
using Flint.Core.Protocol.Payloads;
using static Flint.Core.Tests.CaptureFile;

namespace Flint.Core.Tests;

// Presets read from and written to the owner's amp: join the pieces, unpack, pack again, and
// split again, and every byte must come back as captured.
public class PresetTests
{
    [Fact]
    public void An_uploaded_preset_round_trips_to_the_exact_pieces_the_official_app_sent()
    {
        var pieces = Pieces("preset-upload", Opcode.UploadPreset).Single();
        var (type, slot, preset) = PresetTransfer.Unwrap(PresetTransfer.Join(pieces));

        Assert.Equal((PresetType.Slot, 0), (type, slot));
        Assert.Equal("Clean Rich Reverb", preset.Name);
        Assert.Equal(["bias.noisegate", "Compressor", "Booster", "GK800", "GuitarEQ6", "DelayRe201", "bias.reverb"],
            preset.Modules.Select(m => m.DspId));
        // The app always sends the two trailing floats, at their native defaults here.
        Assert.Equal((Preset.UnsetLoudness, Preset.DefaultExtraGain), (preset.Loudness, preset.ExtraGain));
        AssertSamePieces(pieces, PresetTransfer.Split(PresetTransfer.Wrap(type, slot, preset), PresetTransfer.UploadPieceSize));
    }

    [Fact]
    public void A_downloaded_preset_round_trips_to_the_exact_pieces_the_amp_sent()
    {
        var pieces = Pieces("preset-download", Opcode.Preset).Single();
        var (type, slot, preset) = PresetTransfer.Unwrap(PresetTransfer.Join(pieces));

        Assert.Equal((PresetType.Editing, 0), (type, slot));
        Assert.Equal((120f, 7), (preset.Bpm, preset.Modules.Count));
        Assert.Null(preset.Loudness); // the amp leaves the trailing floats out
        Assert.Equal([0, 1, 2, 3, 4], preset.Modules.Single(m => m.DspId == "GK800").Parameters.Select(p => p.Index));
        AssertSamePieces(pieces, PresetTransfer.Split(PresetTransfer.Wrap(type, slot, preset), PresetTransfer.DownloadPieceSize));
    }

    [Fact]
    public void The_02_2b_checksum_of_each_slot_is_the_byte_sum_of_its_packed_preset()
    {
        // The first session read all 8 checksums, then downloaded slots 1, 2, 5 and 7.
        var chunks = Read("slot-checksums");
        var checksums = UIntArray.Decode(chunks.Single(c => c.Chunk.Op == Opcode.Checksums).Chunk.Payload).Values;
        Assert.Equal(8, checksums.Count);

        var slots = Pieces("slot-checksums", Opcode.Preset).Select(p => PresetTransfer.Unwrap(PresetTransfer.Join(p))).ToList();
        Assert.Equal([1, 2, 5, 7], slots.Select(s => s.Slot));
        Assert.All(slots, s => Assert.Equal(checksums[s.Slot], PresetCodec.Checksum(PresetCodec.Pack(s.Preset))));
    }

    [Fact]
    public void A_preset_with_a_wrong_checksum_or_a_missing_piece_is_rejected()
    {
        var pieces = Pieces("preset-download", Opcode.Preset).Single();
        var message = PresetTransfer.Join(pieces);
        message[^1] ^= 0x01;
        Assert.Throws<FormatException>(() => PresetTransfer.Unwrap(message));
        Assert.Throws<FormatException>(() => PresetTransfer.Join([.. pieces.Where((_, i) => i != 3)]));
        Assert.True(PresetTransfer.IsLast(pieces[^1]));
        Assert.False(PresetTransfer.IsLast(pieces[0]));
    }

    /// <summary>The preset pieces in a capture, one list per message (pieces of one message share a seq).</summary>
    private static List<List<byte[]>> Pieces(string capture, int op) =>
        [.. Read(capture).Where(c => c.Chunk.Op == op).GroupBy(c => c.Chunk.Seq).Select(g => g.Select(c => c.Chunk.Payload).ToList())];

    private static void AssertSamePieces(List<byte[]> expected, List<byte[]> actual) =>
        Assert.Equal(expected.Select(Convert.ToHexString), actual.Select(Convert.ToHexString));
}
