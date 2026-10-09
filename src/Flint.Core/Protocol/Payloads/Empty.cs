namespace Flint.Core.Protocol.Payloads;

/// <summary>CNull: acks (<c>04 xx</c>, <c>05 01</c>) and most reads (<c>02 71</c>, <c>02 72</c>, …) carry nothing.</summary>
public sealed record Empty : IPayload
{
    public static readonly Empty Instance = new();

    public byte[] Encode() => [];
}
