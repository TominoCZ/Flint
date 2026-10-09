using Flint.Core.Protocol;

namespace Flint.Core.Presets;

/// <summary>
/// The packed preset, libpghw's CMpPreset: id, name, version, description and icon strings, BPM,
/// the module array (dspId, active, then [index, [values…]] per knob), and optionally loudness and extra gain.
/// </summary>
public static class PresetCodec
{
    public static byte[] Pack(Preset preset)
    {
        var writer = new MsgPackWriter()
            .String(preset.Id).String(preset.Name).String(preset.Version).String(preset.Description).String(preset.Icon)
            .Float(preset.Bpm)
            .ArrayHeader(preset.Modules.Count);
        foreach (var module in preset.Modules)
        {
            writer.String(module.DspId).Bool(module.Active).ArrayHeader(module.Parameters.Count);
            foreach (var parameter in module.Parameters)
            {
                writer.Int(parameter.Index).ArrayHeader(parameter.Values.Count);
                foreach (var value in parameter.Values) writer.Float(value);
            }
        }
        if (preset.Loudness is not null || preset.ExtraGain is not null)
            writer.Float(preset.Loudness ?? Preset.UnsetLoudness).Float(preset.ExtraGain ?? Preset.DefaultExtraGain);
        return writer.ToArray();
    }

    public static Preset Unpack(byte[] packed)
    {
        var reader = new MsgPackReader(packed);
        string id = reader.ReadString(), name = reader.ReadString(), version = reader.ReadString(),
            description = reader.ReadString(), icon = reader.ReadString();
        var bpm = reader.ReadFloat();
        var modules = Enumerable.Range(0, reader.ReadArrayHeader()).Select(_ => ReadModule(reader)).ToList();
        float? loudness = reader.NextIsFloat ? reader.ReadFloat() : null;
        float? extraGain = reader.NextIsFloat ? reader.ReadFloat() : null;
        return new Preset(id, name, version, description, icon, bpm, modules, loudness, extraGain);
    }

    /// <summary>
    /// The 8-bit sum of the packed bytes: the last byte of every preset transfer, and the per-slot value that
    /// <c>02 2b</c> returns (native calculateDataChecksum).
    /// </summary>
    public static byte Checksum(ReadOnlySpan<byte> packed)
    {
        byte sum = 0;
        foreach (var b in packed) sum += b;
        return sum;
    }

    private static PresetModule ReadModule(MsgPackReader reader)
    {
        var dspId = reader.ReadString();
        var active = reader.ReadBool();
        var parameters = Enumerable.Range(0, reader.ReadArrayHeader()).Select(_ =>
        {
            var index = reader.ReadInt();
            return new PresetParameter(index, [.. Enumerable.Range(0, reader.ReadArrayHeader()).Select(_ => reader.ReadFloat())]);
        }).ToList();
        return new PresetModule(dspId, active, parameters);
    }
}
