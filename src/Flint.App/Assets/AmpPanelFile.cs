using System.Text.Json;

namespace Flint.App.Assets;

/// <summary>
/// <c>Configuration/Amp/JamUp.Amp.&lt;dspId&gt;.json</c>: the amp panel's knobs left to right, each with its knob style and
/// the amp parameter it turns. Three newer amps have no file; the app then uses its catalog style and
/// the order every file has, which <see cref="Default"/> gives.
/// </summary>
internal sealed record AmpPanelFile(IReadOnlyList<AmpPanelKnob> Controls)
{
    /// <summary>Gain, bass, middle, treble, volume: params 0, 3, 2, 1, 4, as in all 33 files.</summary>
    public static AmpPanelFile Default(string knobStyle) => new([.. new[] { 0, 3, 2, 1, 4 }.Select(p => new AmpPanelKnob(knobStyle, p))]);

    public static AmpPanelFile? Load(AssetStore assets, string dspId)
    {
        var path = Path.Combine(assets.Root, "Configuration", "Amp", $"JamUp.Amp.{dspId}.json");
        return File.Exists(path) ? JsonSerializer.Deserialize<AmpPanelFile>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) : null;
    }
}
