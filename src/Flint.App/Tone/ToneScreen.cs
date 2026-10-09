using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Flint.App.Assets;
using Flint.App.Controls;
using Flint.Core.Connection;
using Flint.Core.Layout;
using Flint.Core.Model;
using Flint.Core.Presets;

namespace Flint.App.Tone;

/// <summary>
/// The Tone screen (<c>tone_pager.xml</c>): the top bar with the preset's name, edited mark and channel,
/// the strip, and the selected device's panel, with the big value shown over the top while a knob turns. It shows
/// <see cref="AmpState"/> and turns the user's gestures into <see cref="AmpController"/> calls.
/// </summary>
internal sealed class ToneScreen : Grid
{
    private const double TopBarHeight = 76; // top_bar_container on the phone, less its status bar
    private static readonly IBrush Shade = new SolidColorBrush(Color.Parse("#99000000"));

    private readonly AssetStore _assets;
    private readonly AndroidValues _values;
    private readonly AmpController _amp;
    private readonly Action<string, Func<Task>> _run;
    private readonly ChainStrip _strip;
    private readonly EditorHost _editor;
    private readonly TextBlock _name, _channel, _value;
    private readonly Image _edited, _redDot;
    private readonly Border _valueShade;
    private readonly Grid _editorArea;
    private readonly Control _title, _actions; // what the ▾ list and the ⋮ menu drop down from
    private readonly ScaleTransform _scale;
    private readonly Toast _toast = new() { [RowSpanProperty] = 2 };
    private Control? _overlay;
    private ModelSheet? _sheet;
    private LayoutTransformControl? _sheetHost;
    private int _sheetSlot;
    private Task _picking = Task.CompletedTask;
    private PickerEntry? _wanted;
    private int _selected = 3; // the app opens on the amp

    /// <param name="run">Runs an amp action and reports its failure, as the window does for its own buttons.</param>
    public ToneScreen(AssetStore assets, AndroidValues values, AmpController amp, Action<string, Func<Task>> run)
    {
        (_assets, _values, _amp, _run) = (assets, values, amp, run);
        RowDefinitions = new RowDefinitions("Auto,*");
        ClipToBounds = true;
        // The top bar and strip, laid out in the phone's dp and scaled with the window like the panel below them.
        var header = new Grid { RowDefinitions = new RowDefinitions($"{TopBarHeight.ToString(CultureInfo.InvariantCulture)},{ChainTab.StripHeight.ToString(CultureInfo.InvariantCulture)}") };
        _scale = new ScaleTransform(1, 1);

        if (assets.Image("bg_profile_header") is not null) // the chevrons behind the top, 9:16 of the width as in tone_pager
            Children.Add(new Image { Source = assets.Bitmap("bg_profile_header"), Stretch = Stretch.UniformToFill, VerticalAlignment = VerticalAlignment.Top, [RowSpanProperty] = 2 });

        (_name, _channel, _edited, _redDot) = (Text(20, Brushes.White), Text(14, new SolidColorBrush(values.Color("white70pa"))),
            Picture("ic_foot_switch_editing_mark"), Picture("ic_bpm_red_dot", 8));
        _edited.Margin = new Thickness(2, 6, 0, 0);
        var arrow = Picture("ic_arrow_triangle_solid_down", 16);
        arrow.Margin = new Thickness(6, 0, 12, 0);
        _channel.Margin = new Thickness(4, 2, 0, 0);
        // 12 dp lower than centred in the toolbar (2 dp from its top): without the phone's status bar above it, the name sat
        // against the window's edge. The buttons stay where they are.
        var title = new StackPanel { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(values.Dimen("toolbar_title_start_margin"), 14, 0, 0) };
        title.Children.Add(Row(_name, _edited, arrow));
        title.Children.Add(Row(_redDot, _channel));
        title.Background = Brushes.Transparent; // the whole name, not just its glyphs, opens the list
        Clicks.On(title, OpenPresetList);
        _title = title;
        var cloud = IconButton("btn_tonecloud");
        cloud.Opacity = 0.35; // ToneCloud is out of scope
        var actions = _actions = IconButton("top_btn_save");
        Clicks.On(actions, OpenActions);
        var buttons = Row(cloud, actions);
        buttons.HorizontalAlignment = HorizontalAlignment.Right;
        header.Children.Add(new Grid { Height = values.Dimen("toolbar_height"), VerticalAlignment = VerticalAlignment.Top, Children = { title, buttons } });

        _strip = new ChainStrip(assets) { [RowProperty] = 1 };
        _strip.Chosen += Select;
        _strip.ModelsRequested += slot =>
        {
            Select(slot);
            if (_sheet is null) OpenSheet(slot);
        };
        _strip.Swiped += (slot, on) =>
        {
            if (Module(slot) is { } module && module.Active != on) _run($"switch {module.DspId} {(on ? "on" : "off")}", () => _amp.SetModuleOnAsync(module.DspId, on));
        };
        header.Children.Add(_strip);

        var context = new LayoutContext(_ => null, values.Dimen);
        _editor = new EditorHost(assets, values, context);
        _editor.Turned += (param, value) =>
        {
            if (Module(_selected) is not { } module) return;
            try
            {
                _amp.SetParameter(module.DspId, param, value);
            }
            catch (InvalidOperationException)
            {
                // Not connected: the control moves but nothing is sent; the amp's state wins on reconnect (decision 7).
            }
        };
        _editor.PowerToggled += () =>
        {
            if (Module(_selected) is { } module) _run($"switch {module.DspId}", () => _amp.SetModuleOnAsync(module.DspId, !module.Active));
        };
        _editorArea = new Grid { [RowProperty] = 1, Children = { _editor } };
        Children.Add(_editorArea);

        _value = new TextBlock { FontSize = 58, FontWeight = FontWeight.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 20) };
        _valueShade = new Border { Background = Shade, Child = _value, IsVisible = false, IsHitTestVisible = false, [RowSpanProperty] = 2 };
        header.Children.Add(_valueShade);
        _editor.Showing += text =>
        {
            _valueShade.IsVisible = text is not null;
            if (text is not null) _value.Text = text;
        };
        Children.Add(new LayoutTransformControl { Child = header, LayoutTransform = _scale });

        Children.Add(_toast);
        SizeChanged += (_, e) => _scale.ScaleX = _scale.ScaleY = Scale(e.NewSize.Height);
    }

    /// <summary>
    /// How much bigger than the phone the screen is drawn: the window's height over the phone's Tone screen (top bar,
    /// strip and editor area), the same factor the panel gets from its Viewbox.
    /// </summary>
    public static double Scale(double height) =>
        Math.Clamp(height / (TopBarHeight + ChainTab.StripHeight + EditorHost.DesignHeight), 0.6, 4);

    /// <summary>The chain position shown in the editor: 0 gate … 3 amp … 6 reverb.</summary>
    public int Selected => _selected;

    public void Show(AmpState? state)
    {
        var live = state?.Live;
        _name.Text = live?.Name ?? "";
        _edited.IsVisible = state?.Edited == true;
        _redDot.IsVisible = _channel.IsVisible = state?.CurrentSlot is not null;
        _channel.Text = state?.CurrentSlot is { } slot ? Channel(slot) : "";
        for (var i = 0; i < _assets.Catalog.Chain.Count; i++)
        {
            var module = Module(i);
            _strip.Show(i, module is null ? null : Icon(module), module?.Active ?? false, i == _selected);
        }
        _editor.Show(Module(_selected));
        // The sheet follows the amp the moment the strip does: a pick shows as current once the amp has taken it.
        if (_sheet is not null && Module(_sheetSlot) is { } shown && (shown.DspId != _sheet.CurrentDspId || ReverbOf(shown) is { } r
                && (_sheet.CurrentReverb is not { } was || Math.Abs(r - was) > 0.05f)))
            OpenSheet(_sheetSlot);
    }

    /// <summary>The app's label for a hardware slot under the preset name: "CH1A" for the first.</summary>
    public static string Channel(int slot) => $"CH{slot % 4 + 1}{(slot < 4 ? 'A' : 'B')}";

    /// <summary>Selects a device; with the model sheet open, the sheet switches to it, as on the phone.</summary>
    private void Select(int slot)
    {
        _selected = slot;
        Show(_amp.State);
        if (_sheet is not null) OpenSheet(slot);
    }

    /// <summary>The ⋮ menu. Its saves go through the name dialog, except Overwrite.</summary>
    private void OpenActions()
    {
        if (_amp.State is null) return;
        var menu = new ActionsMenu(_values, _amp.Link == LinkState.Ready);
        var dropdown = new Dropdown(menu, _actions, this, _scale, alignRight: true);
        dropdown.Closed += () => ShowOverlay(null);
        menu.Closed += () => ShowOverlay(null);
        menu.Chosen += action =>
        {
            var name = _amp.State?.Live.Name ?? "";
            switch (action)
            {
                case ToneAction.Overwrite:
                    _run("overwrite", async () =>
                    {
                        await _amp.OverwriteAsync();
                        Toast($"{name} saved");
                    });
                    break;
                case ToneAction.SaveAsNew:
                    AskName(name, 0, (newName, _) => _run("save as new", () =>
                    {
                        _amp.SaveAsNew(newName);
                        Toast($"{newName} saved");
                        return Task.CompletedTask;
                    }));
                    break;
                case ToneAction.SaveToHardware:
                    AskName(name, AmpState.SlotCount, (newName, slot) => _run($"save to {Channel(slot!.Value)}", async () =>
                    {
                        await _amp.SaveToSlotAsync(newName, slot!.Value);
                        Toast($"{newName} saved to {_amp.State?.Name} {Channel(slot.Value)}");
                    }));
                    break;
                case ToneAction.Export when _amp.State is { } exported:
                    _run("export", async () =>
                    {
                        if (await PresetFiles.ExportAsync(TopLevel.GetTopLevel(this)!.StorageProvider, exported.Live)) Toast($"{name} exported");
                    });
                    break;
                case ToneAction.Import:
                    _run("import", async () =>
                    {
                        var (imported, rejected) = await PresetFiles.ImportAsync(TopLevel.GetTopLevel(this)!.StorageProvider, _amp.Import);
                        if (imported + rejected.Count == 0) return;
                        Toast((imported == 1 ? "1 preset imported" : $"{imported} presets imported")
                            + (rejected.Count > 0 ? $"; not a preset: {string.Join(", ", rejected)}" : ""));
                    });
                    break;
            }
        };
        ShowOverlay(dropdown);
    }

    /// <summary>The ▾ list: the amp's slots by bank while connected, then every library preset.</summary>
    private void OpenPresetList()
    {
        if (_amp.State is not { } state) return;
        var connected = _amp.Link == LinkState.Ready;
        var slots = connected
            ? state.Slots.Select((p, i) => new PresetRow(p, i, _amp.EntryOfSlot(i), state.CurrentSlot == i)).ToList()
            : [];
        var library = _amp.LibraryPresets().Select(e => new PresetRow(e.Preset, null, e.Entry, e.Preset.Id == state.Live.Id)).ToList();
        var list = new PresetList(_values, slots, library, Channel, PresetIcon, VectorArt.Load(_assets, _values, "ic_menu_more_vertical"),
            Dropdown.RoomBelow(_title, this, _scale));
        var dropdown = new Dropdown(list, _title, this, _scale, alignRight: false);
        dropdown.Closed += () => ShowOverlay(null);
        list.Closed += () => ShowOverlay(null);
        list.SlotChosen += slot =>
        {
            ShowOverlay(null);
            _run($"select {Channel(slot)}", () => _amp.SelectSlotAsync(slot));
        };
        list.PresetChosen += preset =>
        {
            ShowOverlay(null);
            _run($"load {preset.Name}", () => _amp.LoadAsync(preset));
        };
        list.RenameRequested += (entry, name) =>
        {
            var dialog = new PresetNameDialog(_values, name, 0, Channel, _scale, _values.Text("btn_rename"));
            dialog.Closed += () => Children.Remove(dialog);
            dialog.Saved += (newName, _) => _run($"rename {name}", async () =>
            {
                await _amp.RenameAsync(entry, newName);
                Toast($"{newName} saved");
                OpenPresetList();
            });
            dialog[RowSpanProperty] = 2;
            dialog.ZIndex = 2;
            Children.Add(dialog);
        };
        ShowOverlay(dropdown);
    }

    /// <summary>A preset's picture in the list: its amp's model-sheet icon, standing in for the app's rendered snapshot.</summary>
    private Avalonia.Media.Imaging.Bitmap? PresetIcon(Preset preset)
    {
        var icon = preset.Modules.Select(m => _assets.Catalog.Model(m.DspId)).FirstOrDefault(m => m is not null && _assets.Catalog.Chain[3].Models.Any(x => x.DspId == m.DspId))?.ItemIcon;
        return icon is not null && _assets.Image(icon) is not null ? _assets.Bitmap(icon) : null;
    }

    private void AskName(string name, int slots, Action<string, int?> saved)
    {
        var dialog = new PresetNameDialog(_values, name, slots, Channel, _scale);
        dialog.Closed += () => ShowOverlay(null);
        dialog.Saved += saved;
        ShowOverlay(dialog);
    }

    /// <summary>Shows a menu or dialog over the whole screen, or with null, closes the one shown.</summary>
    private void ShowOverlay(Control? overlay)
    {
        if (_overlay is not null) Children.Remove(_overlay);
        _overlay = overlay;
        if (overlay is null) return;
        overlay[RowSpanProperty] = 2;
        overlay.ZIndex = 1;
        Children.Add(overlay);
    }

    private void Toast(string text) => _toast.Show(text);

    /// <summary>
    /// Opens the model sheet over the panel, laid out at the phone's width and scaled with the window like everything
    /// else, so on a big window it stays the panel's width with the phone's proportions.
    /// </summary>
    private void OpenSheet(int slot)
    {
        if (_sheetHost is not null) _editorArea.Children.Remove(_sheetHost);
        _sheetSlot = slot;
        var module = Module(slot);
        var reverb = module is null ? null : ReverbOf(module);
        _sheet = new ModelSheet(_assets, _assets.Catalog.Chain[slot], module?.DspId, reverb) { Width = EditorHost.DesignWidth };
        _sheet.Picked += entry => Pick(slot, entry);
        _sheetHost = new LayoutTransformControl { Child = _sheet, LayoutTransform = _scale, HorizontalAlignment = HorizontalAlignment.Center };
        _sheet.Closed += () =>
        {
            _editorArea.Children.Remove(_sheetHost);
            (_sheet, _sheetHost) = (null, null);
        };
        _editorArea.Children.Add(_sheetHost);
    }

    /// <summary>
    /// A model picked in the sheet: a reverb type is bias.reverb's param 6; anything else replaces the position's model.
    /// Picks run one after another, each reading the model the amp has when it starts (a pick made before the previous one
    /// lands would swap from the wrong model), and a pick overtaken by a later tap is skipped. The row shows the pick at
    /// once; the sheet stays open on it, as the phone's does.
    /// </summary>
    private void Pick(int slot, PickerEntry entry)
    {
        _wanted = entry;
        _sheet?.MarkPending(entry);
        var next = RunPick(_picking, slot, entry);
        _picking = next;
        _run($"pick {entry.DspId} {entry.ReverbType}", () => next);
    }

    private async Task RunPick(Task previous, int slot, PickerEntry entry)
    {
        try
        {
            await previous;
        }
        catch (Exception)
        {
            // That pick's failure has been shown already; this one still goes.
        }
        try
        {
            if (!ReferenceEquals(_wanted, entry) || Module(slot) is not { } module) return;
            if (entry.ReverbType is { } type)
            {
                if (ReverbOf(module) is not { } current || Math.Abs(current - type) >= 0.05f) await _amp.SetReverbTypeAsync(type);
            }
            else if (entry.DspId != module.DspId)
            {
                await _amp.ReplaceModelAsync(module.DspId, entry.DspId, StartingValues(entry));
            }
        }
        finally
        {
            // Normally the state change has refreshed the sheet already; this clears the bar if nothing changed.
            if (ReferenceEquals(_wanted, entry) && _sheet is { IsPending: true }) OpenSheet(slot);
        }
    }

    private static float? ReverbOf(PresetModule module) =>
        module.DspId == "bias.reverb" ? module.Parameters.FirstOrDefault(p => p.Index == 6)?.Values[0] : null;

    /// <summary>The app's own starting values for a model (<c>ModulePresets/…/data.json</c>), or null if unreadable.</summary>
    private PresetModule? StartingValues(PickerEntry entry)
    {
        try
        {
            return PresetJson.ReadModule(File.ReadAllText(Path.Combine(_assets.Root, entry.Preset)));
        }
        catch (Exception e) when (e is IOException or FormatException)
        {
            return null;
        }
    }

    /// <summary>The live preset's module at a chain position, found through the catalog.</summary>
    private PresetModule? Module(int slot)
    {
        var models = _assets.Catalog.Chain[slot].Models;
        return _amp.State?.Live.Modules.FirstOrDefault(m => models.Any(x => x.DspId == m.DspId));
    }

    private Avalonia.Media.Imaging.Bitmap? Icon(PresetModule module)
    {
        var name = module.DspId == "bias.reverb"
            ? _assets.Catalog.Reverb(module.Parameters.FirstOrDefault(p => p.Index == 6)?.Values[0] ?? 0)?.Icon
            : _assets.Catalog.Model(module.DspId)?.Icon;
        return name is not null && _assets.Image(name) is not null ? _assets.Bitmap(name) : null;
    }

    /// <summary>A drawable as the app shows it: a bitmap, or a vector drawn from its paths.</summary>
    private Image Picture(string drawable, double size = double.NaN) => VectorArt.Picture(_assets, _values, drawable, size);

    /// <summary>
    /// A top-bar button as the app's ImageButton draws it: a 44 dp square with its 24 dp icon centred, not stretched, so
    /// the ⋮'s dots sit 20 dp from the edge as the preset's name does on the other side.
    /// </summary>
    private Border IconButton(string drawable) =>
        new() { Width = 44, Height = 44, Background = Brushes.Transparent, Child = Picture(drawable), VerticalAlignment = VerticalAlignment.Center };

    private static TextBlock Text(double size, IBrush colour) =>
        new() { FontSize = size, FontWeight = FontWeight.SemiBold, Foreground = colour, VerticalAlignment = VerticalAlignment.Center };

    private static StackPanel Row(params Control[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.AddRange(children);
        return row;
    }
}
