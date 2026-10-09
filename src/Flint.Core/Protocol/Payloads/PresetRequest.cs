namespace Flint.Core.Protocol.Payloads;

/// <summary>
/// The download request <c>02 01</c>: the same two values as <see cref="SwitchPreset"/>, but the official app sends
/// them zero-padded to 32 bytes. We do the same, since it is what the amp is known to accept.
/// </summary>
public sealed record PresetRequest(PresetType Type, int Slot) : IPayload
{
    private const int WireLength = 32;

    public static PresetRequest Decode(byte[] payload)
    {
        var reader = new MsgPackReader(payload);
        return new((PresetType)reader.ReadInt(), reader.ReadInt());
    }

    public byte[] Encode()
    {
        var bytes = new byte[WireLength];
        new MsgPackWriter().Int((int)Type).Int(Slot).ToArray().CopyTo(bytes, 0);
        return bytes;
    }
}
