namespace Flint.Core.Protocol;

/// <summary>One message chunk. <see cref="Payload"/> is the raw 8-bit payload, before 7-bit encoding.</summary>
public sealed record Chunk(byte Seq, byte Cmd, byte Sub, byte[] Payload)
{
    /// <summary>cmd and sub as one number, e.g. 0x0138, matching <see cref="Opcode"/> and the opcode tables in the docs.</summary>
    public int Op => Cmd << 8 | Sub;
}

/// <summary>
/// The wire framing, matching libpghw's pg::SysExCommandCodec and SevenBitsCodec:
/// <c>F0 01 seq chk cmd sub &lt;7-bit payload&gt; F7</c>, with chk = XOR of the encoded payload &amp; 0x7F.
/// </summary>
public static class Frame
{
    public const byte Start = 0xF0, Marker = 0x01, End = 0xF7;

    /// <summary>Each group of up to 7 bytes becomes one byte of their high bits (bit j for byte j), then the bytes &amp; 0x7F.</summary>
    public static byte[] EncodeSevenBit(ReadOnlySpan<byte> raw)
    {
        var output = new byte[raw.Length + (raw.Length + 6) / 7];
        var o = 0;
        for (var i = 0; i < raw.Length; i += 7)
        {
            var group = raw.Slice(i, Math.Min(7, raw.Length - i));
            var highBits = o++;
            for (var j = 0; j < group.Length; j++)
            {
                if ((group[j] & 0x80) != 0) output[highBits] |= (byte)(1 << j);
                output[o++] = (byte)(group[j] & 0x7F);
            }
        }
        return output;
    }

    public static byte[] DecodeSevenBit(ReadOnlySpan<byte> encoded)
    {
        var output = new List<byte>(encoded.Length);
        for (var i = 0; i < encoded.Length; i += 8)
        {
            var highBits = encoded[i];
            for (var j = 1; j < 8 && i + j < encoded.Length; j++)
                output.Add((byte)(encoded[i + j] | ((highBits >> (j - 1)) & 1) << 7));
        }
        return [.. output];
    }

    public static byte Checksum(ReadOnlySpan<byte> encodedPayload)
    {
        byte x = 0;
        foreach (var b in encodedPayload) x ^= b;
        return (byte)(x & 0x7F);
    }

    public static byte[] EncodeChunk(Chunk chunk)
    {
        var payload = EncodeSevenBit(chunk.Payload);
        return [Start, Marker, chunk.Seq, Checksum(payload), chunk.Cmd, chunk.Sub, .. payload, End];
    }

    /// <summary>Decodes exactly one chunk, F0 through F7. False if it is malformed or its checksum is wrong.</summary>
    public static bool TryDecodeChunk(ReadOnlySpan<byte> wire, out Chunk? chunk)
    {
        chunk = null;
        if (wire.Length < 7 || wire[0] != Start || wire[1] != Marker || wire[^1] != End) return false;
        var payload = wire[6..^1];
        if (Checksum(payload) != wire[3]) return false;
        chunk = new Chunk(wire[2], wire[4], wire[5], DecodeSevenBit(payload));
        return true;
    }

    /// <summary>
    /// The 16-byte block header around chunks sent to the amp. Spark 2 over BLE expects it on every write, while
    /// its own notifications come as bare chunks.
    /// </summary>
    public static byte[] WrapBlock(ReadOnlySpan<byte> chunks) =>
        [0x01, 0xFE, 0x00, 0x00, 0x53, 0xFE, checked((byte)(16 + chunks.Length)), 0, 0, 0, 0, 0, 0, 0, 0, 0, .. chunks];
}
