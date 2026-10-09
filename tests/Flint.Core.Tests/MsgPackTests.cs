using Flint.Core.Protocol;

namespace Flint.Core.Tests;

// The captures only exercise the short forms, so the width boundaries cwpack switches at are pinned here.
public class MsgPackTests
{
    [Theory]
    [InlineData(0u, "00")]
    [InlineData(127u, "7F")]
    [InlineData(128u, "CC80")]
    [InlineData(255u, "CCFF")]
    [InlineData(256u, "CD0100")]
    [InlineData(65536u, "CE00010000")]
    public void Unsigned_ints_use_the_shortest_form(uint value, string hex)
    {
        Assert.Equal(hex, Convert.ToHexString(new MsgPackWriter().UInt(value).ToArray()));
        Assert.Equal(value, new MsgPackReader(Convert.FromHexString(hex)).ReadUInt());
    }

    [Theory]
    [InlineData(-1, "FF")]
    [InlineData(-32, "E0")]
    [InlineData(-33, "D0DF")]
    [InlineData(-99, "D09D")]
    [InlineData(-129, "D1FF7F")]
    [InlineData(-40000, "D2FFFF63C0")]
    [InlineData(5, "05")]
    public void Signed_ints_use_the_shortest_form(int value, string hex)
    {
        Assert.Equal(hex, Convert.ToHexString(new MsgPackWriter().Int(value).ToArray()));
        Assert.Equal(value, new MsgPackReader(Convert.FromHexString(hex)).ReadInt());
    }

    [Fact]
    public void A_32_bit_int_is_read_as_cwpack_does_whatever_its_sign()
    {
        Assert.Equal(unchecked((int)3622231501u), new MsgPackReader(Convert.FromHexString("CED7E6DDCD")).ReadInt());
        Assert.Equal(uint.MaxValue, new MsgPackReader(Convert.FromHexString("FF")).ReadUInt());
        Assert.Throws<FormatException>(() => new MsgPackReader(Convert.FromHexString("CF0000000100000000")).ReadInt());
    }

    [Theory]
    [InlineData(31, "BF")]
    [InlineData(32, "D920")]
    [InlineData(300, "DA012C")]
    public void Strings_switch_from_fixstr_to_str8_at_32_bytes(int length, string header)
    {
        var text = new string('x', length);
        var bytes = new MsgPackWriter().String(text).ToArray();
        Assert.StartsWith(header, Convert.ToHexString(bytes));
        Assert.Equal(text, new MsgPackReader(bytes).ReadString());
    }

    [Fact]
    public void Arrays_switch_to_array16_at_16_entries()
    {
        Assert.Equal("9F", Convert.ToHexString(new MsgPackWriter().ArrayHeader(15).ToArray()));
        Assert.Equal("DC0010", Convert.ToHexString(new MsgPackWriter().ArrayHeader(16).ToArray()));
        Assert.Equal(16, new MsgPackReader(Convert.FromHexString("DC0010")).ReadArrayHeader());
    }

    [Fact]
    public void A_prefixed_name_is_its_length_then_the_string()
    {
        Assert.Equal("05A5474B383030", Convert.ToHexString(new MsgPackWriter().Prefixed("GK800").ToArray()));
        Assert.Throws<FormatException>(() => new MsgPackReader(Convert.FromHexString("04A5474B383030")).ReadPrefixed());
    }

    [Fact]
    public void A_wrong_type_or_a_short_payload_throws_instead_of_misreading()
    {
        Assert.Throws<FormatException>(() => new MsgPackReader([0xC3]).ReadFloat());
        Assert.Throws<FormatException>(() => new MsgPackReader([0xCA, 0x3F]).ReadFloat());
    }
}
