using Flint.Core.Protocol.Payloads;

namespace Flint.Core.Presets;

/// <summary>
/// How a preset crosses the link. The message is <c>&lt;type&gt; &lt;slot&gt;</c>, the packed preset,
/// then its <see cref="PresetCodec.Checksum"/>. It travels in pieces, each chunk payload being
/// <c>&lt;total&gt; &lt;index&gt; &lt;length&gt;</c> and then that many bytes: 128 per piece when we upload (<c>01 01</c>, one seq
/// for all, each acked with <c>05 01</c>), 25 when the amp answers a download (<c>02 01</c> → <c>03 01</c>).
/// </summary>
public static class PresetTransfer
{
    public const int UploadPieceSize = 128, DownloadPieceSize = 25;

    /// <summary>
    /// The preset as we upload it: with both trailing floats, as the official app always sends them. The amp stores an
    /// upload as sent and <c>02 2b</c> then reports the checksum of this form.
    /// </summary>
    public static Preset ForUpload(Preset preset) =>
        preset with { Loudness = preset.Loudness ?? Preset.UnsetLoudness, ExtraGain = preset.ExtraGain ?? Preset.DefaultExtraGain };

    /// <summary>What <c>02 2b</c> reports for a slot once this preset is uploaded to it: the byte sum of <see cref="ForUpload"/>'s form.</summary>
    public static uint UploadChecksum(Preset preset) => PresetCodec.Checksum(PresetCodec.Pack(ForUpload(preset)));

    public static byte[] Wrap(PresetType type, int slot, Preset preset)
    {
        var packed = PresetCodec.Pack(preset);
        return [(byte)type, checked((byte)slot), .. packed, PresetCodec.Checksum(packed)];
    }

    /// <summary>Throws <see cref="FormatException"/> if the checksum is wrong, as the native unpack does.</summary>
    public static (PresetType Type, int Slot, Preset Preset) Unwrap(byte[] message)
    {
        if (message.Length < 4) throw new FormatException($"a preset message of {message.Length} bytes is too short");
        var packed = message.AsSpan(2, message.Length - 3);
        if (PresetCodec.Checksum(packed) != message[^1])
            throw new FormatException($"preset checksum {message[^1]:X2} does not match its data ({PresetCodec.Checksum(packed):X2})");
        return ((PresetType)message[0], message[1], PresetCodec.Unpack(packed.ToArray()));
    }

    public static List<byte[]> Split(byte[] message, int pieceSize)
    {
        var total = checked((byte)((message.Length + pieceSize - 1) / pieceSize));
        return [.. message.Chunk(pieceSize).Select((piece, index) => (byte[])[total, (byte)index, (byte)piece.Length, .. piece])];
    }

    /// <summary>True for the piece that completes a message, so a receiver knows when to <see cref="Join"/>.</summary>
    public static bool IsLast(byte[] piece) => piece.Length >= 2 && piece[1] == piece[0] - 1;

    /// <summary>Joins the pieces of one message, checking they are all there, in order, and the lengths they claim.</summary>
    public static byte[] Join(IReadOnlyList<byte[]> pieces)
    {
        for (var i = 0; i < pieces.Count; i++)
        {
            var piece = pieces[i];
            if (piece.Length < 3 || piece[0] != pieces.Count || piece[1] != i || piece[2] != piece.Length - 3)
                throw new FormatException($"preset piece {i} of {pieces.Count} is malformed: {Convert.ToHexString(piece[..Math.Min(3, piece.Length)])}");
        }
        return [.. pieces.SelectMany(p => p[3..])];
    }
}
