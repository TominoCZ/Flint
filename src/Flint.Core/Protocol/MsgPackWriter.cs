using System.Text;

namespace Flint.Core.Protocol;

/// <summary>
/// Writes payload values the way libpghw's cwpack calls do: always the shortest msgpack
/// form, because the amp's checksums and our byte-for-byte capture tests depend on the exact bytes.
/// </summary>
public sealed class MsgPackWriter
{
    private readonly List<byte> _bytes = [];

    public int Length => _bytes.Count;

    public byte[] ToArray() => [.. _bytes];

    public MsgPackWriter Bool(bool value)
    {
        _bytes.Add(value ? (byte)0xC3 : (byte)0xC2);
        return this;
    }

    public MsgPackWriter UInt(uint value)
    {
        if (value < 0x80) _bytes.Add((byte)value);
        else if (value <= byte.MaxValue) _bytes.AddRange([0xCC, (byte)value]);
        else if (value <= ushort.MaxValue) BigEndian(0xCD, value, 2);
        else BigEndian(0xCE, value, 4);
        return this;
    }

    /// <summary>cw_pack_signed: non-negative values are written as unsigned.</summary>
    public MsgPackWriter Int(int value)
    {
        if (value >= 0) return UInt((uint)value);
        if (value >= -32) _bytes.Add((byte)value);
        else if (value >= sbyte.MinValue) _bytes.AddRange([0xD0, (byte)value]);
        else if (value >= short.MinValue) BigEndian(0xD1, (ushort)value, 2);
        else BigEndian(0xD2, (uint)value, 4);
        return this;
    }

    public MsgPackWriter Float(float value)
    {
        BigEndian(0xCA, BitConverter.SingleToUInt32Bits(value), 4);
        return this;
    }

    public MsgPackWriter String(string value)
    {
        var text = Encoding.UTF8.GetBytes(value);
        if (text.Length < 32) _bytes.Add((byte)(0xA0 | text.Length));
        else if (text.Length <= byte.MaxValue) _bytes.AddRange([0xD9, (byte)text.Length]);
        else BigEndian(0xDA, checked((ushort)text.Length), 2);
        _bytes.AddRange(text);
        return this;
    }

    public MsgPackWriter ArrayHeader(int count)
    {
        if (count < 16) _bytes.Add((byte)(0x90 | count));
        else BigEndian(0xDC, checked((ushort)count), 2);
        return this;
    }

    /// <summary>An effect name in a command: its byte length as a uint, then the string (<c>05 A5 "GK800"</c>).</summary>
    public MsgPackWriter Prefixed(string value) => UInt((uint)Encoding.UTF8.GetByteCount(value)).String(value);

    private void BigEndian(byte tag, uint value, int size)
    {
        _bytes.Add(tag);
        for (var shift = 8 * (size - 1); shift >= 0; shift -= 8) _bytes.Add((byte)(value >> shift));
    }
}
