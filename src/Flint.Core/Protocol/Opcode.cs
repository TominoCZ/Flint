namespace Flint.Core.Protocol;

/// <summary>
/// The opcodes Spark 2 is known to use, as <c>cmd &lt;&lt; 8 | sub</c> (see <see cref="Chunk.Op"/>). Commands: 01 set,
/// 02 get, 03 reply or notify, 04 ack, 05 upload-chunk ack. Only opcodes seen in a Spark 2 capture are named here;
/// gen-1 tables from Spark 40 projects are not to be trusted for Spark 2.
/// </summary>
public static class Opcode
{
    // Presets: multi-chunk, see Presets/PresetTransfer.
    public const int UploadPreset = 0x0101, UploadPresetAck = 0x0501, DownloadPreset = 0x0201, Preset = 0x0301;

    // The signal chain. The amp's own knobs and pedal switches arrive as 03 37 and 03 15.
    public const int SetParameter = 0x0104, AmpParameter = 0x0337;
    public const int SetModuleOn = 0x0115, ModuleOn = 0x0315;
    public const int ReplaceModule = 0x0106;

    // Slots. 03 38 is the amp's own preset selector.
    public const int SelectSlot = 0x0138, AmpSlot = 0x0338;
    public const int GetSlotStates = 0x021A, SlotStates = 0x031A;
    public const int GetChecksums = 0x022B, Checksums = 0x032B;

    // The device.
    public const int GetSystemInfo = 0x0271, SystemInfo = 0x0371;
    public const int SetSettings = 0x0172, GetSettings = 0x0272, Settings = 0x0372;
    public const int SetName = 0x0111, GetName = 0x0211, Name = 0x0311;
    public const int GetSerial = 0x0223, Serial = 0x0323;
    public const int GetFirmware = 0x022F, Firmware = 0x032F;
    public const int SetLineOutMute = 0x01A2, GetLineOutMute = 0x02A2, LineOutMute = 0x03A2;
    public const int GetInOutLink = 0x02A8; // LIVE/EDGE only: Spark 2 never answers
    public const int GetControlPedalConnected = 0x0296, ControlPedalConnected = 0x0396; // Spark Control X, read at connect

    // Tuner and looper. 03 65 is the amp's own tuner button; 03 75 and 03 77 come from its looper buttons.
    public const int SetTuner = 0x0165, GetTuner = 0x0265, Tuner = 0x0365, TunerReading = 0x0364;
    public const int SetLooperAction = 0x0175, GetLooperAction = 0x0275, LooperState = 0x0375, LooperProgress = 0x0377;
    public const int SetLooperTimeSignature = 0x0176, GetLooperTimeSignature = 0x0276, LooperTimeSignature = 0x0376;
}
