namespace Flint.Core.Protocol.Payloads;

/// <summary>CInt32Array: the slot-states request (<c>02 1a</c> [0]) and the per-slot checksums (<c>03 2b</c>).</summary>
public sealed record UIntArray(IReadOnlyList<uint> Values) : IPayload
{
    public static UIntArray Decode(byte[] payload)
    {
        var reader = new MsgPackReader(payload);
        return new([.. Enumerable.Range(0, reader.ReadArrayHeader()).Select(_ => reader.ReadUInt())]);
    }

    public byte[] Encode()
    {
        var writer = new MsgPackWriter().ArrayHeader(Values.Count);
        foreach (var value in Values) writer.UInt(value);
        return writer.ToArray();
    }
}
