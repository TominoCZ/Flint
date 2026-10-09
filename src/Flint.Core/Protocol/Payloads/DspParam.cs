namespace Flint.Core.Protocol.Payloads;

/// <summary>
/// CSetDspParam: one knob of one module, 0.0 to 1.0. We send it as <c>01 04</c> (no ack), and the amp reports its own
/// knobs the same way as <c>03 37</c>. The trailing signal-chain index is always 0 on Spark 2.
/// </summary>
public sealed record DspParam(string Module, int Param, float Value, int SignalChain = 0) : IPayload
{
    public static DspParam Decode(byte[] payload)
    {
        var reader = new MsgPackReader(payload);
        return new(reader.ReadPrefixed(), reader.ReadInt(), reader.ReadFloat(), reader.ReadInt());
    }

    public byte[] Encode() => new MsgPackWriter().Prefixed(Module).Int(Param).Float(Value).Int(SignalChain).ToArray();
}
