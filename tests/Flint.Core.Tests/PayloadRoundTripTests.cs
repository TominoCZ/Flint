using Flint.Core.Protocol;
using Flint.Core.Protocol.Payloads;
using static Flint.Core.Tests.CaptureFile;

namespace Flint.Core.Tests;

// Every captured message decodes to a typed payload and encodes back to the same bytes.
public class PayloadRoundTripTests
{
    [Fact]
    public void Every_captured_payload_decodes_and_re_encodes_to_the_same_bytes()
    {
        foreach (var file in Directory.GetFiles("Captures", "*.txt"))
        foreach (var (_, chunk) in Read(Path.GetFileNameWithoutExtension(file)))
        {
            if (chunk.Op is Opcode.UploadPreset or Opcode.Preset) continue; // PresetTests cover the pieces
            var payload = PayloadDecoder.Decode(chunk);
            Assert.True(payload is not null, $"{file}: no payload type for {chunk.Op:x4}");
            Assert.Equal(Convert.ToHexString(chunk.Payload), Convert.ToHexString(payload.Encode()));
        }
    }

    [Fact]
    public void Device_replies_decode_to_their_fields()
    {
        // From the first session's connect handshake; the serial reply is left out on purpose.
        // Shutdown before standby: SettingInfoPacker's order. The owner's amp: off after 1 h, standby after 15 min.
        Assert.Equal(new SettingInfo(VoiceGuide: false, AutoShutdown: 60, AudioJackMode: 0, AutoStandby: 15),
            SettingInfo.Decode(Convert.FromHexString("C23C000F")));
        Assert.Equal(new LooperTimeSignature(120, 4, 4, false, true, false, 60),
            LooperTimeSignature.Decode(Convert.FromHexString("780404C2C3C23C")));
        Assert.Equal("Spark 2", PrefixedText.Decode(Convert.FromHexString("07A7537061726B2032")).Text);
        Assert.Equal(0x020702C8u, UIntValue.Decode(Convert.FromHexString("CE020702C8")).Value); // firmware 2.7.2.200

        var info = SystemInfo.Decode(Convert.FromHexString("060001010000D09D"));
        Assert.Equal(new SystemInfo(SystemState: 6, A2dpState: 0, AcState: 1, ChargingState: 1, BatteryVoltage: 0,
            TemperatureAdc: 0, Temperature: -99), info);
        Assert.Equal("060001010000D09D", Convert.ToHexString(info.Encode()));
    }

    [Fact]
    public void Slot_states_flatten_three_values_per_entry_under_one_array_header()
    {
        var states = SlotStates.Decode(Convert.FromHexString("9100CE17CDB2D7C2")).States;
        Assert.Equal([new SlotState(0, 0x17CDB2D7, false)], states);
        Assert.Equal([new SlotState(0, 7, true)], SlotStates.Decode(Convert.FromHexString("910007C3")).States);
    }

    [Fact]
    public void A_preset_request_is_padded_to_32_bytes_like_the_official_app()
    {
        var bytes = new PresetRequest(PresetType.Editing, 0).Encode();
        Assert.Equal(32, bytes.Length);
        Assert.Equal("0100" + new string('0', 60), Convert.ToHexString(bytes));
    }
}
