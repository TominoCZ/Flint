using System.Xml;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Flint.App.Assets;
using Flint.App.Controls;
using Flint.Core.Layout;
using Flint.Core.Logging;
using Flint.Core.Presets;

namespace Flint.App.Tone;

/// <summary>
/// The selected device's panel below the strip (<c>editor_pager</c>). Panels are laid out at the phone's editor size and
/// scaled to fit, so a bigger window shows a bigger panel. A panel is kept while its model stays,
/// so a knob being turned is never rebuilt under the mouse.
/// </summary>
internal sealed class EditorHost : Viewbox
{
    /// <summary>The owner's phone's editor area, in dp: what the panels were checked against.</summary>
    public const double DesignWidth = 360, DesignHeight = 1331 / 3.0;

    private readonly AssetStore _assets;
    private readonly AndroidValues _values;
    private readonly LayoutContext _context;
    private string? _shownKey;
    private Action<PresetModule>? _show;

    public EditorHost(AssetStore assets, AndroidValues values, LayoutContext context)
    {
        (_assets, _values, _context) = (assets, values, context);
        Stretch = Avalonia.Media.Stretch.Uniform;
        VerticalAlignment = VerticalAlignment.Top;
    }

    /// <summary>A control of the shown panel was turned: (param index, value 0–1).</summary>
    public event Action<int, float>? Turned;

    /// <summary>The big value to show while a control is held, or null to hide it.</summary>
    public event Action<string?>? Showing;

    /// <summary>The shown panel's power switch or footswitch was pressed.</summary>
    public event Action? PowerToggled;

    public void Show(PresetModule? module)
    {
        var model = module is null ? null : _assets.Catalog.Model(module.DspId);
        var reverb = module?.DspId == "bias.reverb"
            ? _assets.Catalog.Reverb(module.Parameters.FirstOrDefault(p => p.Index == 6)?.Values[0] ?? 0)
            : null;
        var key = $"{module?.DspId}/{reverb?.Value}";
        if (key != _shownKey)
        {
            _shownKey = key;
            _show = null;
            Child = model is null || model.Editor.Count == 0 ? null : BuildOrNote(model, reverb);
        }
        if (module is not null) _show?.Invoke(module);
    }

    /// <summary>The panel, or, when its layout or knob files are gone or broken, a note saying so instead of a crash.</summary>
    private Control BuildOrNote(CatalogModel model, ReverbType? reverb)
    {
        try
        {
            return Build(model, reverb);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException or XmlException)
        {
            Log.Warn("ui", $"{model.Name ?? model.DspId}'s panel cannot be drawn: {e.Message}");
            _show = null;
            return new Border
            {
                Width = DesignWidth, Height = DesignHeight,
                Child = new TextBlock
                {
                    Text = $"{model.Name ?? model.DspId}: its artwork is missing.\nUpdate the Spark artwork from the tray icon's menu.",
                    TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Foreground = Brushes.Gray, FontSize = 14,
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Avalonia.Thickness(24),
                },
            };
        }
    }

    private Control Build(CatalogModel model, ReverbType? reverb)
    {
        Control panel;
        if (model.Face is not null && model.Editor[0].StartsWith("editor_amp"))
        {
            var amp = new AmpPanel(_assets, _values, model, _context);
            amp.Turned += (param, value) => Turned?.Invoke(param, value);
            amp.Showing += text => Showing?.Invoke(text);
            amp.PowerToggled += () => PowerToggled?.Invoke();
            (_show, panel) = (amp.Show, amp);
        }
        else
        {
            var pedal = new PedalPanel(_assets, _values, model.Editor[0], reverb?.Face, _context);
            pedal.Turned += (param, value) => Turned?.Invoke(param, value);
            pedal.Showing += text => Showing?.Invoke(text);
            pedal.PowerToggled += () => PowerToggled?.Invoke();
            (_show, panel) = (pedal.Show, pedal);
        }
        return new Border { Width = DesignWidth, Height = DesignHeight, Child = panel };
    }
}
