namespace Flint.Core.Protocol.Payloads;

/// <summary>Picks the payload type a chunk carries from its opcode, as libpghw's session templates do.</summary>
public static class PayloadDecoder
{
    /// <summary>
    /// The typed payload, or null for preset pieces (<see cref="Opcode.UploadPreset"/>, <see cref="Opcode.Preset"/>:
    /// reassemble those with PresetTransfer) and for opcodes we have not mapped yet. Throws
    /// <see cref="FormatException"/> if a known opcode carries a payload of the wrong shape.
    /// </summary>
    public static IPayload? Decode(Chunk chunk)
    {
        var p = chunk.Payload;
        return chunk.Op switch
        {
            Opcode.UploadPreset or Opcode.Preset => null,
            Opcode.SetParameter or Opcode.AmpParameter => DspParam.Decode(p),
            Opcode.SetModuleOn or Opcode.ModuleOn => DspOnOff.Decode(p),
            Opcode.ReplaceModule => DspReplace.Decode(p),
            Opcode.SelectSlot or Opcode.AmpSlot => SwitchPreset.Decode(p),
            Opcode.DownloadPreset => PresetRequest.Decode(p),
            Opcode.SlotStates => SlotStates.Decode(p),
            Opcode.GetSlotStates or Opcode.Checksums => UIntArray.Decode(p),
            Opcode.GetChecksums or Opcode.Firmware or Opcode.SetLooperAction or Opcode.LooperState or Opcode.GetInOutLink => UIntValue.Decode(p),
            Opcode.SystemInfo => SystemInfo.Decode(p),
            Opcode.SetSettings or Opcode.Settings => SettingInfo.Decode(p),
            Opcode.SetName or Opcode.Name or Opcode.Serial => PrefixedText.Decode(p),
            Opcode.SetTuner or Opcode.Tuner or Opcode.SetLineOutMute or Opcode.LineOutMute or Opcode.ControlPedalConnected
                => BoolValue.Decode(p),
            Opcode.TunerReading => TunerReading.Decode(p),
            Opcode.LooperProgress => FloatValue.Decode(p),
            Opcode.SetLooperTimeSignature or Opcode.LooperTimeSignature => LooperTimeSignature.Decode(p),
            _ when p.Length == 0 => Empty.Instance,
            _ => null,
        };
    }
}
