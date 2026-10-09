using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Flint.App.Assets;

namespace Flint.App.Tone;

/// <summary>
/// The signal-chain strip (<c>sigpath</c> in <c>tone_pager.xml</c>): one <see cref="ChainTab"/> per chain position on the
/// red cable, 175 dp tall. A narrow window scrolls it sideways, as the phone does; a wide one shows it all, centred.
/// </summary>
internal sealed class ChainStrip : Grid
{
    private readonly List<ChainTab> _tabs = [];
    private readonly ScrollViewer _scroll;
    private int _centred = -1;

    public ChainStrip(AssetStore assets)
    {
        Height = ChainTab.StripHeight;
        if (assets.Image("signal_path_line") is not null)
            Children.Add(new Image
            {
                Source = assets.Bitmap("signal_path_line"), Stretch = Stretch.Fill, Height = 20, VerticalAlignment = VerticalAlignment.Center,
            });
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var (slot, index) in assets.Catalog.Chain.Select((s, i) => (s, i)))
        {
            var tab = new ChainTab(slot.Label, index);
            tab.Chosen += t => Chosen?.Invoke(t.Slot);
            tab.ModelsRequested += t => ModelsRequested?.Invoke(t.Slot);
            tab.Swiped += (t, on) => Swiped?.Invoke(t.Slot, on);
            _tabs.Add(tab);
            row.Children.Add(tab);
        }
        Children.Add(_scroll = new ScrollViewer
        {
            Content = row, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        });
        _scroll.SizeChanged += (_, _) => Centre(_centred, force: true);
    }

    public event Action<int>? Chosen, ModelsRequested;

    public event Action<int, bool>? Swiped;

    public void Show(int slot, Avalonia.Media.Imaging.Bitmap? icon, bool on, bool selected)
    {
        _tabs[slot].Show(icon, on, selected);
        if (selected) Centre(slot, force: false);
    }

    /// <summary>Scrolls the selected device to the middle, as the phone's strip does when one is picked.</summary>
    private void Centre(int slot, bool force)
    {
        if (slot < 0 || (slot == _centred && !force)) return;
        _centred = slot;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var tab = _tabs[slot];
            var x = tab.Bounds.X + tab.Bounds.Width / 2 - _scroll.Viewport.Width / 2;
            _scroll.Offset = new Avalonia.Vector(Math.Clamp(x, 0, Math.Max(0, _scroll.Extent.Width - _scroll.Viewport.Width)), 0);
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }
}
