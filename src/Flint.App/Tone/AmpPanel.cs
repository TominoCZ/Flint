using Avalonia.Controls;
using Avalonia.Media;
using Flint.App.Assets;
using Flint.App.Controls;
using Flint.Core.Layout;
using Flint.Core.Presets;

namespace Flint.App.Tone;

/// <summary>
/// An amp's panel as the app draws it: <c>editor_amp.xml</c> (or the model's own layout) with the model's face and cab,
/// its knobs from the amp's panel file, and the power switch drawn on the face. Knob N turns the param the panel file
/// gives it; the amp's own knob moves are shown through <see cref="Show"/>, except on a knob being held.
/// </summary>
internal sealed class AmpPanel : Decorator
{
    private readonly List<(KnobControl Knob, int Param)> _knobs = [];

    public AmpPanel(AssetStore assets, AndroidValues values, CatalogModel amp, LayoutContext context)
    {
        DspId = amp.DspId;
        var file = AmpPanelFile.Load(assets, amp.DspId) ?? AmpPanelFile.Default(amp.KnobStyle ?? "JamUpKnob_01");
        var layout = LayoutNode.Load(assets.Xml("layout", amp.Editor[0]) ?? throw new FileNotFoundException($"no layout {amp.Editor[0]}"), values.Style);
        Color glowStart = values.Color("konb_highlight_color_start"), glowEnd = values.Color("konb_highlight_color_end");
        var knobIndex = 0;
        Child = new LayoutView(layout, context, node => node.Tag switch
        {
            "ImageView" when node.Id == "amp_background" && amp.Face is { } face => new Image { Source = assets.Bitmap(face), Stretch = Stretch.Fill },
            "ImageView" when node.Id == "amp_cab" && amp.Cab is { } cab => new Image { Source = assets.Bitmap(cab), Stretch = Stretch.Fill },
            "Knob" when knobIndex < file.Controls.Count => NewKnob(file.Controls[knobIndex++]),
            "ToggleButton" when node.Id == "switch_power" => NewPowerSwitch(),
            _ => null,
        });

        Control NewKnob(AmpPanelKnob spec)
        {
            var knob = new KnobControl(KnobArt.Load(assets, spec.KnobType), glowStart, glowEnd);
            knob.Turned += value =>
            {
                Turned?.Invoke(spec.Param, (float)value);
                Showing?.Invoke(PanelDisplay.Knob(value));
            };
            knob.Held += held => Showing?.Invoke(held ? PanelDisplay.Knob(knob.Value) : null);
            _knobs.Add((knob, spec.Param));
            return knob;
        }

        Control NewPowerSwitch()
        {
            // Transparent in the app too: the switch is part of the face image.
            var hotspot = new Border { Background = Brushes.Transparent, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
            hotspot.PointerPressed += (_, e) =>
            {
                PowerToggled?.Invoke();
                e.Handled = true;
            };
            return hotspot;
        }
    }

    public string DspId { get; }

    /// <summary>A knob was turned by the user: (param index, value 0–1).</summary>
    public event Action<int, float>? Turned;

    /// <summary>The big value to show while a knob is held, or null to hide it.</summary>
    public event Action<string?>? Showing;

    public event Action? PowerToggled;

    /// <summary>Moves every knob not being held to the module's values.</summary>
    public void Show(PresetModule module)
    {
        foreach (var (knob, param) in _knobs)
            if (!knob.IsHeld && module.Parameters.FirstOrDefault(p => p.Index == param) is { Values.Count: > 0 } value)
                knob.Value = value.Values[0];
    }
}
