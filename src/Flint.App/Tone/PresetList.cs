using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Flint.App.Assets;
using Flint.App.Controls;
using Flint.Core.Presets;

namespace Flint.App.Tone;

/// <summary>One row of the ▾ list: a slot's preset (<see cref="Slot"/> set) or a library one. <see cref="Entry"/> is null when it cannot be renamed.</summary>
internal sealed record PresetRow(Preset Preset, int? Slot, string? Entry, bool Current);

/// <summary>
/// The ▾ preset list (<c>fragment_preset_manager.xml</c>), as a dropdown under the preset's name rather
/// than the phone's full-screen page: the tabs "HW Bank A", "HW Bank B" (only while connected) and
/// "All", and the chosen tab's rows (<c>fragment_preset_list_item.xml</c>, at about half the phone's height), the
/// current one in dark red. It opens on the current preset's tab. A row's ⋮ renames it, which the app does from a
/// slide-in button. The app's categories and Delete are left out.
/// </summary>
internal sealed class PresetList : Border
{
    private static readonly IBrush Sheet = new SolidColorBrush(Color.Parse("#1c1c1c")), Row = new SolidColorBrush(Color.Parse("#101010")),
        Line = new SolidColorBrush(Color.Parse("#333333")), Separator = new SolidColorBrush(Color.Parse("#15ffffff")),
        Red = new SolidColorBrush(Color.Parse("#e02020")), Grey = new SolidColorBrush(Color.Parse("#9e9e9e")),
        Faint = new SolidColorBrush(Color.Parse("#80ffffff")),
        CurrentRow = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.Parse("#46272a"), 0), new GradientStop(Color.Parse("#391b1d"), 1) },
        };

    private readonly Func<int, string> _label;
    private readonly Func<Preset, Bitmap?> _icon;
    private readonly IImage? _more;
    private readonly List<(List<PresetRow> Rows, Border Tab)> _tabs = [];
    private readonly StackPanel _rows = new();

    /// <param name="slots">The amp's 8 slots, or empty when not connected (the hardware tabs are then left out).</param>
    /// <param name="library">The library's presets for the All tab.</param>
    /// <param name="maxHeight">The room below the name, in dp: the rows scroll within it.</param>
    public PresetList(AndroidValues values, IReadOnlyList<PresetRow> slots, IReadOnlyList<PresetRow> library, Func<int, string> label,
        Func<Preset, Bitmap?> icon, IImage? more, double maxHeight)
    {
        (Width, MaxHeight) = (400, Math.Max(200, maxHeight));
        (_label, _icon, _more) = (label, icon, more);
        Background = Sheet;
        if (slots.Count > 0)
        {
            AddTab("HW Bank A", [.. slots.Where(r => r.Slot < 4)]);
            AddTab("HW Bank B", [.. slots.Where(r => r.Slot >= 4)]);
        }
        AddTab(values.Text("category_tab_all"), [.. library]);

        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Height = 52, Margin = new Thickness(3, 0) };
        foreach (var tab in _tabs) tabs.Children.Add(tab.Tab);

        var column = new DockPanel();
        DockPanel.SetDock(tabs, Dock.Top);
        var line = new Border { Height = 2, Background = Separator };
        DockPanel.SetDock(line, Dock.Top);
        column.Children.AddRange([tabs, line, new ScrollViewer { Content = _rows }]);
        Child = column;

        // The hardware tabs come first, so a slot's preset opens on its bank and any other on All.
        var opening = _tabs.FindIndex(t => t.Rows.Any(r => r.Current));
        Show(opening >= 0 ? opening : _tabs.Count - 1);
    }

    /// <summary>A slot's row was tapped: select that slot.</summary>
    public event Action<int>? SlotChosen;

    /// <summary>A library row was tapped: play that preset.</summary>
    public event Action<Preset>? PresetChosen;

    /// <summary>A row's ⋮: rename that library entry.</summary>
    public event Action<string, string>? RenameRequested;

    /// <summary>The current preset's row was clicked: nothing to change, so the list just closes.</summary>
    public event Action? Closed;

    private void AddTab(string name, List<PresetRow> rows)
    {
        var index = _tabs.Count;
        var tab = new Border
        {
            Height = 32, Padding = new Thickness(24, 0), Margin = new Thickness(5, 0), CornerRadius = new CornerRadius(16), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = name, FontSize = 12, VerticalAlignment = VerticalAlignment.Center },
        };
        Clicks.On(tab, () => Show(index));
        _tabs.Add((rows, tab));
    }

    /// <summary>Selects a tab: filled red; a tab holding the current preset but not shown is outlined red, as the app marks it.</summary>
    private void Show(int index)
    {
        for (var i = 0; i < _tabs.Count; i++)
        {
            var (rows, tab) = _tabs[i];
            var holdsCurrent = rows.Any(r => r.Current);
            tab.Background = i == index ? Red : Brushes.Transparent;
            tab.BorderBrush = i != index && holdsCurrent ? Red : null;
            tab.BorderThickness = new Thickness(i != index && holdsCurrent ? 1 : 0);
            ((TextBlock)tab.Child!).Foreground = i == index ? Brushes.White : holdsCurrent ? Red : Grey;
        }
        _rows.Children.Clear();
        foreach (var row in _tabs[index].Rows) _rows.Children.Add(RowOf(row));
    }

    private Border RowOf(PresetRow row)
    {
        var grid = new Grid { Height = 64, ColumnDefinitions = new ColumnDefinitions("88,*,44") };
        if (_icon(row.Preset) is { } bitmap)
            grid.Children.Add(new Image { Source = bitmap, Margin = new Thickness(12, 8), Stretch = Stretch.Uniform });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, [Grid.ColumnProperty] = 1 };
        if (row.Slot is { } slot) text.Children.Add(new TextBlock { Text = _label(slot), FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = Faint });
        text.Children.Add(new TextBlock
        {
            Text = row.Preset.Name, FontSize = 16, FontWeight = FontWeight.Bold, Foreground = Brushes.White, LetterSpacing = 0.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        grid.Children.Add(text);
        if (row.Entry is { } entry)
        {
            var more = new Image { Source = _more, Width = 24, Height = 24, Margin = new Thickness(0, 0, 20, 0), [Grid.ColumnProperty] = 2 };
            Clicks.On(more, () => RenameRequested?.Invoke(entry, row.Preset.Name));
            grid.Children.Add(more);
        }
        var border = new Border
        {
            Background = row.Current ? CurrentRow : Row, BorderBrush = Line, BorderThickness = new Thickness(0, 0, 0, 1), Child = grid,
        };
        Clicks.On(border, () =>
        {
            if (row.Current) Closed?.Invoke();
            else if (row.Slot is { } s) SlotChosen?.Invoke(s);
            else PresetChosen?.Invoke(row.Preset);
        });
        return border;
    }
}
