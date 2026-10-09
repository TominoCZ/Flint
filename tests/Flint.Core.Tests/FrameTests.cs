using Flint.Core.Protocol;

namespace Flint.Core.Tests;

public class FrameTests
{
    // First chunk of a preset reply from a real Spark 40 (cmd 03 sub 01), taken from soundshed-app's
    // src/core/tests/messageParsingTest.ts. It is the only hardware-produced vector we have until the
    // Spark 2 captures exist.
    private const string RealPresetChunk =
        "f0013a640301200f001900015924004533394545313600412d444531302d00343634362d3832f7";

    // Decoded by hand: 15 chunks, index 0, 25 bytes; preset header 00 01; D9 24 = a 36-char string (the UUID).
    private const string RealPresetPayload = "0f00190001d92445333945453136412d444531302d343634362d3832";

    [Fact]
    public void Decodes_a_real_amp_chunk_and_its_checksum()
    {
        Assert.True(Frame.TryDecodeChunk(Convert.FromHexString(RealPresetChunk), out var chunk));
        Assert.Equal((0x3A, 0x03, 0x01), (chunk!.Seq, chunk.Cmd, chunk.Sub));
        Assert.Equal(RealPresetPayload, Convert.ToHexStringLower(chunk.Payload));
    }

    [Fact]
    public void Re_encoding_the_real_chunk_gives_the_same_bytes()
    {
        Frame.TryDecodeChunk(Convert.FromHexString(RealPresetChunk), out var chunk);
        Assert.Equal(RealPresetChunk, Convert.ToHexStringLower(Frame.EncodeChunk(chunk!)));
    }

    [Fact]
    public void Rejects_a_chunk_with_a_wrong_checksum()
    {
        var bytes = Convert.FromHexString(RealPresetChunk);
        bytes[3] ^= 0x01;
        Assert.False(Frame.TryDecodeChunk(bytes, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(10)] // a partial last group is not padded: n + ceil(n/7) bytes, as libpghw's expectedEncodedSize
    public void Seven_bit_round_trips_and_has_the_native_length(int length)
    {
        var raw = Enumerable.Range(0, length).Select(i => (byte)(i * 37 + 0x80)).ToArray();
        var encoded = Frame.EncodeSevenBit(raw);
        Assert.Equal(length + (length + 6) / 7, encoded.Length);
        Assert.All(encoded, b => Assert.True(b < 0x80));
        Assert.Equal(raw, Frame.DecodeSevenBit(encoded));
    }

    [Fact]
    public void Wraps_a_chunk_in_the_documented_block_header()
    {
        // Switch to hardware preset 2 (gen-1 01 38), as framed by spark-web's tests. Only the header layout is
        // checked here: 01 FE 00 00 53 FE, total length, nine zeros.
        var chunk = Frame.EncodeChunk(new Chunk(0x3A, 0x01, 0x38, [0x00, 0x02]));
        Assert.Equal("01fe000053fe1a000000000000000000f0013a020138000002f7",
            Convert.ToHexStringLower(Frame.WrapBlock(chunk)));
    }
}
