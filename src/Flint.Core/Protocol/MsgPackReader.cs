using System.Buffers.Binary;
using System.Text;

namespace Flint.Core.Protocol;

/// <summary>
/// Reads the msgpack values payloads are made of. Every read checks the type tag and
/// throws <see cref="FormatException"/> on anything else, so a misread payload fails loudly instead of drifting.
/// </summary>
public sealed class MsgPackReader(byte[] data)
{
    public int Position { get; private set; }

    public bool AtEnd => Position >= data.Length;

    public bool ReadBool() => Next() switch
    {
        0xC2 => false,
        0xC3 => true,
        var tag => throw Unexpected(tag, "bool"),
    };

    // cwpack's unpack_int32 and unpack_uint32 keep the low 32 bits of any integer, signed or not: the amp once sent
    // 3622231501 where the app's type says int32. ReadInteger only takes 32-bit forms.
    public uint ReadUInt() => unchecked((uint)ReadInteger());

    public int ReadInt() => unchecked((int)ReadInteger());

    public float ReadFloat()
    {
        var tag = Next();
        return tag == 0xCA ? BinaryPrimitives.ReadSingleBigEndian(Take(4)) : throw Unexpected(tag, "float32");
    }

    /// <summary>True if the next value is a float32, for the optional fields at the end of a preset.</summary>
    public bool NextIsFloat => !AtEnd && data[Position] == 0xCA;

    public string ReadString()
    {
        var tag = Next();
        var length = tag switch
        {
            >= 0xA0 and <= 0xBF => tag & 0x1F,
            0xD9 => Next(),
            0xDA => BinaryPrimitives.ReadUInt16BigEndian(Take(2)),
            _ => throw Unexpected(tag, "string"),
        };
        return Encoding.UTF8.GetString(Take(length));
    }

    public int ReadArrayHeader()
    {
        var tag = Next();
        return tag switch
        {
            >= 0x90 and <= 0x9F => tag & 0x0F,
            0xDC => BinaryPrimitives.ReadUInt16BigEndian(Take(2)),
            _ => throw Unexpected(tag, "array"),
        };
    }

    /// <summary>An effect name in a command: a uint byte length, then a string of that length.</summary>
    public string ReadPrefixed()
    {
        var length = ReadUInt();
        var text = ReadString();
        return Encoding.UTF8.GetByteCount(text) == length ? text : throw new FormatException($"prefix {length} does not match \"{text}\"");
    }

    private long ReadInteger()
    {
        var tag = Next();
        return tag switch
        {
            < 0x80 => tag,
            >= 0xE0 => (sbyte)tag,
            0xCC => Next(),
            0xCD => BinaryPrimitives.ReadUInt16BigEndian(Take(2)),
            0xCE => BinaryPrimitives.ReadUInt32BigEndian(Take(4)),
            0xD0 => (sbyte)Next(),
            0xD1 => BinaryPrimitives.ReadInt16BigEndian(Take(2)),
            0xD2 => BinaryPrimitives.ReadInt32BigEndian(Take(4)),
            _ => throw Unexpected(tag, "integer"),
        };
    }

    private byte Next() => Take(1)[0];

    private ReadOnlySpan<byte> Take(int count)
    {
        if (Position + count > data.Length)
            throw new FormatException($"payload ends at {data.Length}, needed {count} more byte(s) at {Position}");
        var span = data.AsSpan(Position, count);
        Position += count;
        return span;
    }

    private FormatException Unexpected(byte tag, string expected) =>
        new($"expected {expected} at {Position - 1}, found 0x{tag:X2}");
}
