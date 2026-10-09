using Avalonia.Controls;
using Avalonia.Media;
using Flint.App.Assets;
using Flint.App.Controls;
using Flint.Core.Layout;
using Flint.Core.Presets;

namespace Flint.App.Tone;

/// <summary>
/// A pedal's panel (<c>editor_*.xml</c>), working as the app's: every knob, slider and switch turns the parameter the
/// app's data binding gives it (the catalog's <c>panels</c>), the footswitch and the LED switch the
/// pedal, and all of them follow the amp. A control the catalog does not map is drawn but left still.
/// </summary>
internal sealed class PedalPanel : Decorator
{
    private readonly List<(KnobControl Knob, int Param)> _knobs = [];
    private readonly List<(SliderControl Slider, int Param)> _sliders = [];
    private readonly List<(Image Image, SelectorArt Art, int? Param)> _switches = []; // Param null: the power LED
    private PresetModule? _module;

    /// <param name="face">Replaces the layout's own face (<c>fx_background</c>): a reverb's face depends on its type.</param>
    public PedalPanel(AssetStore assets, AndroidValues values, string layoutName, string? face, LayoutContext context)
    {
        var layout = LayoutNode.Load(assets.Xml("layout", layoutName) ?? throw new FileNotFoundException($"no layout {layoutName}"), values.Style);
        Color glowStart = values.Color("konb_highlight_color_start"), glowEnd = values.Color("konb_highlight_color_end");
        Child = new LayoutView(layout, context, node => node.Tag switch
        {
            "ImageView" => ImageFor(node.Id == "fx_background" && face is not null ? face : Drawable(node["srcCompat"] ?? node["src"])),
            "Knob" => NewKnob(node),
            "Slider" => NewSlider(node),
            "ToggleButton" => NewSwitch(node),
            _ => null,
        });

        Control? ImageFor(string? name) =>
            name is not null && assets.Image(name) is not null ? new Image { Source = assets.Bitmap(name), Stretch = Stretch.Fill } : null;

        PanelControl? Mapped(LayoutNode node) => assets.Catalog.Control(layoutName, node.Id, node["tag"]);

        Control NewKnob(LayoutNode node)
        {
            var knob = new KnobControl(KnobArt.Load(assets, node["knobStyle"] ?? "jamUp_01"), glowStart, glowEnd);
            if (Mapped(node) is not { } control)
            {
                knob.IsHitTestVisible = false;
                return knob;
            }
            _knobs.Add((knob, control.Param));
            knob.Turned += value =>
            {
                Turned?.Invoke(control.Param, (float)value);
                Showing?.Invoke(PanelDisplay.Knob(value));
            };
            knob.Held += held => Showing?.Invoke(held ? PanelDisplay.Knob(knob.Value) : null);
            return knob;
        }

        Control NewSlider(LayoutNode node)
        {
            var slider = new SliderControl(node.Attributes, name => assets.Image(name) is not null ? assets.Bitmap(name) : null);
            if (Mapped(node) is not { } control)
            {
                slider.IsHitTestVisible = false;
                return slider;
            }
            _sliders.Add((slider, control.Param));
            slider.Moved += value =>
            {
                Turned?.Invoke(control.Param, (float)value);
                Showing?.Invoke(slider.IsHeld ? PanelDisplay.Slider(value) : null);
            };
            slider.Held += held => Showing?.Invoke(held ? PanelDisplay.Slider(slider.Value) : null);
            return slider;
        }

        Control? NewSwitch(LayoutNode node)
        {
            // switch_power (the LED), button_power (an invisible area over the footswitch) and the wahs' power_toggle
            // switch the pedal. A mapped switch (a delay's tempo sync) flips its 0/1 parameter.
            var isPower = node.Id is "switch_power" or "button_power" or "power_toggle";
            var param = isPower ? null : Mapped(node) is { Kind: "switch" } c ? c.Param : (int?)null;
            var art = Drawable(node["background"]) is { } name ? SelectorArt.Load(assets, name) : null;
            Control control;
            if (art is not null)
            {
                var image = new Image { Stretch = Stretch.Fill, Source = art.Unchecked };
                if (isPower && node.Id != "button_power" || param is not null) _switches.Add((image, art, param));
                control = image;
            }
            else if (isPower)
            {
                control = new Border { Background = Brushes.Transparent };
            }
            else
            {
                return null;
            }
            if (!isPower && param is null) return control; // drawn, but nothing known to switch
            control.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
            control.PointerPressed += (_, e) =>
            {
                if (isPower) PowerToggled?.Invoke();
                else Turned?.Invoke(param!.Value, IsOn(param.Value) ? 0f : 1f);
                e.Handled = true;
            };
            return control;
        }
    }

    /// <summary>A control was turned: (param index, value 0–1).</summary>
    public event Action<int, float>? Turned;

    /// <summary>The big value to show while a control is held, or null to hide it.</summary>
    public event Action<string?>? Showing;

    /// <summary>The footswitch or the LED was pressed.</summary>
    public event Action? PowerToggled;

    public void Show(PresetModule module)
    {
        _module = module;
        foreach (var (knob, param) in _knobs)
            if (!knob.IsHeld && Value(param) is { } v) knob.Value = v;
        foreach (var (slider, param) in _sliders)
            if (!slider.IsHeld && Value(param) is { } v) slider.Value = v;
        foreach (var (image, art, param) in _switches)
            image.Source = (param is { } p ? IsOn(p) : module.Active) ? art.Checked : art.Unchecked;
    }

    private float? Value(int param) =>
        _module?.Parameters.FirstOrDefault(p => p.Index == param) is { Values.Count: > 0 } value ? value.Values[0] : null;

    private bool IsOn(int param) => Value(param) >= 0.5f;

    private static string? Drawable(string? reference) => reference is not null && reference.StartsWith("@drawable/") ? reference[10..] : null;
}
