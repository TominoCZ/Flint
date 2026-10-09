namespace Flint.Core.Protocol.Payloads;

/// <summary>CLooperTimeSignature (<c>03 76</c>): the looper's tempo and length. The amp sends one after every slot switch.</summary>
public sealed record LooperTimeSignature(
    uint Bpm, uint Beats, uint Bars, bool FreeLength, bool CountIn, bool CreativeLooper, uint MaxRecordingLength) : IPayload
{
    public static LooperTimeSignature Decode(byte[] payload)
    {
        var reader = new MsgPackReader(payload);
        return new(reader.ReadUInt(), reader.ReadUInt(), reader.ReadUInt(),
            reader.ReadBool(), reader.ReadBool(), reader.ReadBool(), reader.ReadUInt());
    }

    public byte[] Encode() => new MsgPackWriter().UInt(Bpm).UInt(Beats).UInt(Bars)
        .Bool(FreeLength).Bool(CountIn).Bool(CreativeLooper).UInt(MaxRecordingLength).ToArray();
}
