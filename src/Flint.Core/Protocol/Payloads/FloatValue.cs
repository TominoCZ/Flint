namespace Flint.Core.Protocol.Payloads;

/// <summary>CFloat: looper progress (<c>03 77</c>), gain ratio, BPM.</summary>
public sealed record FloatValue(float Value) : IPayload
{
    public static FloatValue Decode(byte[] payload) => new(new MsgPackReader(payload).ReadFloat());

    public byte[] Encode() => new MsgPackWriter().Float(Value).ToArray();
}
