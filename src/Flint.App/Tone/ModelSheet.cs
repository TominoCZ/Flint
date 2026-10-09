using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Flint.App.Assets;
using Flint.App.Controls;

namespace Flint.App.Tone;

/// <summary>
/// The model sheet a double-tap opens: a header naming the chain position that closes it, then one row
/// per model in <c>order.json</c>'s order, numbered as shown, the current one in dark red. Spark 2's sheet hides the
/// J.H. pack, the vocal models and the preamps. Tapping a row picks that model (<see cref="Picked"/>).
/// </summary>
internal sealed class ModelSheet : Border
{
    private static readonly IBrush Gold = new SolidColorBrush(Color.Parse("#deb636"));
    private readonly List<(PickerEntry Entry, Border Row)> _rows = [];
    private bool _pending;

    private static readonly IBrush Current = new SolidColorBrush(Color.Parse("#442528")), Header = new SolidColorBrush(Color.Parse("#222224")),
        Sheet = new SolidColorBrush(Color.Parse("#101010"));

    public ModelSheet(AssetStore assets, ChainSlot slot, string? currentDspId, float? currentReverb)
    {
        (CurrentDspId, CurrentReverb) = (currentDspId, currentReverb);
        // Rounded at the top like its header, and clipped to it, so no square corner of the sheet shows past the header's.
        (Background, CornerRadius, ClipToBounds) = (Sheet, new CornerRadius(16, 16, 0, 0), true);
        var panel = new DockPanel();
        Child = panel;
        var close = new Button
        {
            Height = 52, HorizontalAlignment = HorizontalAlignment.Stretch, Background = Header, CornerRadius = new CornerRadius(16, 16, 0, 0),
            Content = new Grid
            {
                Children =
                {
                    new TextBlock { Text = slot.Label, FontWeight = FontWeight.Bold, FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock { Text = "⌄", FontSize = 22, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 6) },
                },
            },
        };
        close.Click += (_, _) => Closed?.Invoke();
        DockPanel.SetDock(close, Dock.Top);
        panel.Children.Add(close);

        var rows = new StackPanel();
        Control? currentRow = null;
        var number = 0;
        foreach (var entry in slot.Order)
        {
            var model = assets.Catalog.Model(entry.DspId);
            if (Hidden(entry, model)) continue;
            var reverb = entry.ReverbType is { } v ? assets.Catalog.ReverbTypes.FirstOrDefault(t => Math.Abs(t.Value - v) < 0.05f) : null;
            var name = reverb?.Name ?? model?.Name ?? entry.DspId;
            var icon = reverb?.ItemIcon ?? model?.ItemIcon;
            var isCurrent = entry.ReverbType is { } type ? currentDspId == "bias.reverb" && currentReverb is { } r && Math.Abs(r - type) < 0.05f
                : entry.DspId == currentDspId;
            var row = Row(assets, ++number, name, icon, isCurrent);
            var picked = entry;
            Clicks.On(row, () => Picked?.Invoke(picked));
            _rows.Add((entry, row));
            if (isCurrent) currentRow = row;
            rows.Children.Add(row);
        }
        var scroll = new ScrollViewer { Content = rows };
        panel.Children.Add(scroll);
        // Opens on the current model, as the app does.
        if (currentRow is not null)
            scroll.LayoutUpdated += ScrollToCurrent;
        void ScrollToCurrent(object? sender, EventArgs e)
        {
            if (scroll.Viewport.Height <= 0) return;
            scroll.LayoutUpdated -= ScrollToCurrent;
            scroll.Offset = new Vector(0, Math.Max(0, currentRow!.Bounds.Y - (scroll.Viewport.Height - currentRow.Bounds.Height) / 2));
        }
    }

    public event Action? Closed;

    /// <summary>The model this sheet shows as current, so the screen can tell when the amp has moved on.</summary>
    public string? CurrentDspId { get; }

    public float? CurrentReverb { get; }

    /// <summary>A pick is under way (<see cref="MarkPending"/>) and not yet shown as current.</summary>
    public bool IsPending => _pending;

    /// <summary>A row was tapped: the model, or for the reverb, the reverb type.</summary>
    public event Action<PickerEntry>? Picked;

    /// <summary>
    /// Shows a pick as under way until the amp has it: the row is highlighted at once and a gold bar runs along its foot.
    /// A later tap moves the mark to its row; the screen opens a fresh sheet once the amp has the model.
    /// </summary>
    public void MarkPending(PickerEntry entry)
    {
        _pending = true;
        foreach (var (e, row) in _rows)
        {
            row.Background = e == entry ? Current : Brushes.Transparent;
            if (row.Child is Grid g) g.Children.RemoveAll([.. g.Children.OfType<ProgressBar>()]);
            if (e == entry && row.Child is Grid grid)
                grid.Children.Add(new ProgressBar
                {
                    IsIndeterminate = true, Height = 3, MinHeight = 3, VerticalAlignment = VerticalAlignment.Bottom, Foreground = Gold,
                    Background = Brushes.Transparent, [Grid.ColumnSpanProperty] = 2,
                });
        }
    }

    private static bool Hidden(PickerEntry entry, CatalogModel? model) =>
        entry.DspId.StartsWith("JH.") || entry.DspId.Contains("Vocal") || model?.Editor.FirstOrDefault()?.StartsWith("editor_preamp") == true;

    private static Border Row(AssetStore assets, int number, string name, string? icon, bool current)
    {
        var grid = new Grid { Height = 163, ColumnDefinitions = new ColumnDefinitions("186,*") };
        if (icon is not null && assets.Image(icon) is not null)
            grid.Children.Add(new Image { Source = assets.Bitmap(icon), Width = 130, Height = 115, Margin = new Thickness(30, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Left });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, [Grid.ColumnProperty] = 1 };
        text.Children.Add(new TextBlock { Text = number.ToString("00"), FontSize = 40, FontWeight = FontWeight.Bold });
        text.Children.Add(new TextBlock { Text = name, FontSize = 16, FontWeight = FontWeight.SemiBold });
        grid.Children.Add(text);
        return new Border
        {
            Background = current ? Current : Brushes.Transparent, Margin = new Thickness(10, 0), Child = grid,
        };
    }
}
