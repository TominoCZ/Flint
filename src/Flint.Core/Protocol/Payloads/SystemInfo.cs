namespace Flint.Core.Protocol.Payloads;

/// <summary>
/// pg::SysInfoPacker (<c>03 71</c>): six uints and one int, in the order <c>SysInfoPacker::unpack</c> reads them and the
/// JNI copies them to the app's fields. SystemState read 0 just after a power-cycle and 6 once
/// the amp was up. Doubles as the keepalive read.
/// </summary>
public sealed record SystemInfo(
    uint SystemState, uint A2dpState, uint AcState, uint ChargingState, uint BatteryVoltage, uint TemperatureAdc,
    int Temperature) : IPayload
{
    public static SystemInfo Decode(byte[] payload)
    {
        var reader = new MsgPackReader(payload);
        return new(reader.ReadUInt(), reader.ReadUInt(), reader.ReadUInt(), reader.ReadUInt(), reader.ReadUInt(),
            reader.ReadUInt(), reader.ReadInt());
    }

    public byte[] Encode() => new MsgPackWriter().UInt(SystemState).UInt(A2dpState).UInt(AcState).UInt(ChargingState)
        .UInt(BatteryVoltage).UInt(TemperatureAdc).Int(Temperature).ToArray();
}
