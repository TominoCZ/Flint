namespace Flint.Core.Protocol.Payloads;

/// <summary>
/// CSwitchPreset: select a slot (<c>01 38</c>, acked <c>04 38</c>); the amp's own preset selector reports the same
/// shape as <c>03 38</c>. The amp follows every switch with an unrelated <c>03 76</c>.
/// </summary>
public sealed record SwitchPreset(PresetType Type, int Slot) : IPayload
{
    public static SwitchPreset Decode(byte[] payload)
    {
        var reader = new MsgPackReader(payload);
        return new((PresetType)reader.ReadInt(), reader.ReadInt());
    }

    public byte[] Encode() => new MsgPackWriter().Int((int)Type).Int(Slot).ToArray();
}
