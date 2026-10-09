namespace Flint.Core.Protocol.Payloads;

/// <summary>
/// pg::SettingInfoPacker (<c>03 72</c>, written with <c>01 72</c>): the device page's Power Settings. Minutes for the
/// timers, 0 for never. The wire order is the packer's, not the Java API's.
/// </summary>
public sealed record SettingInfo(bool VoiceGuide, uint AutoShutdown, uint AudioJackMode, uint AutoStandby) : IPayload
{
    public static SettingInfo Decode(byte[] payload)
    {
        var reader = new MsgPackReader(payload);
        return new(reader.ReadBool(), reader.ReadUInt(), reader.ReadUInt(), reader.ReadUInt());
    }

    public byte[] Encode() =>
        new MsgPackWriter().Bool(VoiceGuide).UInt(AutoShutdown).UInt(AudioJackMode).UInt(AutoStandby).ToArray();
}
