namespace Flint.Core.Protocol.Payloads;

/// <summary>A length-prefixed string: the amp's name (<c>03 11</c>) and serial number (<c>03 23</c>).</summary>
public sealed record PrefixedText(string Text) : IPayload
{
    public static PrefixedText Decode(byte[] payload) => new(new MsgPackReader(payload).ReadPrefixed());

    public byte[] Encode() => new MsgPackWriter().Prefixed(Text).ToArray();
}
