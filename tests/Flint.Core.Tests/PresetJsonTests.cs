using Flint.Core.Presets;
using Flint.Core.Protocol;
using static Flint.Core.Tests.CaptureFile;

namespace Flint.Core.Tests;

// The local library stores the official app's JSON, so a preset must survive amp → JSON → amp unchanged.
public class PresetJsonTests
{
    [Theory]
    [InlineData("preset-download", Opcode.Preset)]
    [InlineData("preset-upload", Opcode.UploadPreset)]
    public void A_captured_preset_survives_json_byte_for_byte(string capture, int op)
    {
        var pieces = Read(capture).Where(c => c.Chunk.Op == op).Select(c => c.Chunk.Payload).ToList();
        var preset = PresetTransfer.Unwrap(PresetTransfer.Join(pieces)).Preset;

        var again = PresetJson.Read(PresetJson.Write(preset));

        Assert.Equal(Convert.ToHexString(PresetCodec.Pack(preset)), Convert.ToHexString(PresetCodec.Pack(again)));
    }

    [Fact]
    public void Reads_the_phone_apps_shape_with_bool_and_array_values_and_no_loudness()
    {
        // The shape of the app's preset.json files; the values are made up.
        const string json = """
            {"bpm": 96, "type": "jamup_speaker",
             "meta": {"id": "0000-TEST", "name": "Test Tone", "version": "0.7", "icon": "icon.png", "description": ""},
             "sigpath": [{"dspId": "UniVibe", "active": true, "type": "speaker_fx",
                          "params": [{"index": 0, "value": 0.25}, {"index": 1, "value": false}, {"value": [0.1, 0.2], "index": 2}]}]}
            """;
        var preset = PresetJson.Read(json);

        Assert.Equal(("Test Tone", 96f, (float?)null), (preset.Name, preset.Bpm, preset.Loudness));
        var module = Assert.Single(preset.Modules);
        Assert.Equal(("UniVibe", true), (module.DspId, module.Active));
        Assert.Equal([[0.25f], [0f], [0.1f, 0.2f]], module.Parameters.Select(p => p.Values.ToArray()));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"bpm": 120, "sigpath": []}""")]
    [InlineData("""{"bpm": "fast", "meta": {}, "sigpath": []}""")]
    [InlineData("""{"bpm": 120, "meta": {"name": 5}, "sigpath": []}""")]
    [InlineData("""{"bpm": 120, "meta": {}, "sigpath": [{"dspId": "GK800", "params": [{"index": 0}]}]}""")]
    public void A_malformed_file_is_a_format_exception(string json) =>
        Assert.Throws<FormatException>(() => PresetJson.Read(json));
}
