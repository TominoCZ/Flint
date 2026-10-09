namespace Flint.Core.Presets;

/// <summary>
/// A preset as libpghw's CMpPreset holds it. <see cref="Loudness"/> and <see cref="ExtraGain"/> are null when the
/// packed form left them out, as presets downloaded from the amp do; the official app always sends both on upload.
/// </summary>
public sealed record Preset(
    string Id,
    string Name,
    string Version,
    string Description,
    string Icon,
    float Bpm,
    IReadOnlyList<PresetModule> Modules,
    float? Loudness = null,
    float? ExtraGain = null)
{
    /// <summary>The native default for <see cref="Loudness"/>, meaning "not set": the app leaves it out of the JSON.</summary>
    public const float UnsetLoudness = 9990f;

    /// <summary>The native default for <see cref="ExtraGain"/>.</summary>
    public const float DefaultExtraGain = 0.5f;

    /// <summary>The longest name the official app's name field takes (<c>popup_edit_preset</c>'s maxLength).</summary>
    public const int MaxNameLength = 40;

    /// <summary>This preset as a new one: a fresh id, as the app's Save as New and Save to hardware give it, and a name.</summary>
    public Preset AsNew(string name) => this with { Id = Guid.NewGuid().ToString(), Name = ValidName(name) };

    /// <summary>The name trimmed, or <see cref="ArgumentException"/> if it is blank or longer than the app allows.</summary>
    public static string ValidName(string name) =>
        name.Trim() is { Length: > 0 and <= MaxNameLength } trimmed ? trimmed : throw new ArgumentException($"a preset name takes 1 to {MaxNameLength} characters");

    /// <summary>This preset with one knob of one module set; a knob the module did not list yet is added.</summary>
    public Preset WithParameter(string dspId, int index, float value) => WithModule(dspId, m => m with
    {
        Parameters = m.Parameters.Any(p => p.Index == index)
            ? [.. m.Parameters.Select(p => p.Index == index ? p with { Values = [value] } : p)]
            : [.. m.Parameters, new PresetParameter(index, [value])],
    });

    public Preset WithModuleOn(string dspId, bool on) => WithModule(dspId, m => m with { Active = on });

    /// <summary>This preset with one module swapped for another in the same place.</summary>
    public Preset WithModuleReplaced(string dspId, PresetModule replacement) => WithModule(dspId, _ => replacement);

    private Preset WithModule(string dspId, Func<PresetModule, PresetModule> change) =>
        this with { Modules = [.. Modules.Select(m => m.DspId == dspId ? change(m) : m)] };
}
