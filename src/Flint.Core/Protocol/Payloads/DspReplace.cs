namespace Flint.Core.Protocol.Payloads;

/// <summary>
/// CReplaceDspModule: swap a module's model (<c>01 06</c>, acked <c>04 06</c>). The official app follows it with a
/// <c>01 15</c> switching the new model on. The reverb type is not a swap: it is param 6 of <c>bias.reverb</c>.
/// </summary>
public sealed record DspReplace(string From, string To, int SignalChain = 0) : IPayload
{
    public static DspReplace Decode(byte[] payload)
    {
        var reader = new MsgPackReader(payload);
        return new(reader.ReadPrefixed(), reader.ReadPrefixed(), reader.ReadInt());
    }

    public byte[] Encode() => new MsgPackWriter().Prefixed(From).Prefixed(To).Int(SignalChain).ToArray();
}
