namespace Flint.Core.Protocol.Payloads;

/// <summary>CBool: tuner on/off (<c>01 65</c>, <c>03 65</c>), line-out mute behaviour (<c>01 a2</c>, <c>03 a2</c>), and the other toggles.</summary>
public sealed record BoolValue(bool Value) : IPayload
{
    public static BoolValue Decode(byte[] payload) => new(new MsgPackReader(payload).ReadBool());

    public byte[] Encode() => new MsgPackWriter().Bool(Value).ToArray();
}
