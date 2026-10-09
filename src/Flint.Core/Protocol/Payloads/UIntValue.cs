namespace Flint.Core.Protocol.Payloads;

/// <summary>CInt32: looper state (<c>03 75</c>), firmware (<c>03 2f</c>, one byte per part), checksum sync request (<c>02 2b</c>).</summary>
public sealed record UIntValue(uint Value) : IPayload
{
    public static UIntValue Decode(byte[] payload) => new(new MsgPackReader(payload).ReadUInt());

    public byte[] Encode() => new MsgPackWriter().UInt(Value).ToArray();
}
