namespace Flint.Core.Protocol.Payloads;

/// <summary>CSetDspOnOff: a pedal on or off. Sent as <c>01 15</c> (acked <c>04 15</c>), reported by the amp as <c>03 15</c>.</summary>
public sealed record DspOnOff(string Module, bool On, int SignalChain = 0) : IPayload
{
    public static DspOnOff Decode(byte[] payload)
    {
        var reader = new MsgPackReader(payload);
        return new(reader.ReadPrefixed(), reader.ReadBool(), reader.ReadInt());
    }

    public byte[] Encode() => new MsgPackWriter().Prefixed(Module).Bool(On).Int(SignalChain).ToArray();
}
