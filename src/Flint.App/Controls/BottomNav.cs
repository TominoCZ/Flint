using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Flint.App.Assets;

namespace Flint.App.Controls;

/// <summary>The tabs that work here; the app's AI and Video tabs are drawn dimmed.</summary>
internal enum AppTab
{
    Tone,
    Music,
    Menu,
}

/// <summary>
/// The app's tab bar (<c>activity_main.xml</c>'s BottomNavigationView, <c>bottom_navigation_menu.xml</c>): five unlabelled
/// 48 dp icons on <c>tab_background_dark</c>, 56 dp tall, the selected one in its <c>_on</c> art (red underline).
/// </summary>
internal sealed class BottomNav : Grid
{
    private static readonly (AppTab? Tab, string Icon)[] Items =
        [(AppTab.Tone, "ic_tone"), (AppTab.Music, "ic_music"), (null, "ic_ai_tone"), (null, "ic_video"), (AppTab.Menu, "ic_profile")];

    private readonly List<(AppTab Tab, Image Image, IImage? Off, IImage? On)> _tabs = [];

    public BottomNav(AssetStore assets, AndroidValues values)
    {
        Height = 56; // design_bottom_navigation_height
        Background = new SolidColorBrush(values.Color("tab_background_dark"));
        ColumnDefinitions = new ColumnDefinitions(string.Join(",", Items.Select(_ => "*")));
        for (var i = 0; i < Items.Length; i++)
        {
            var (tab, icon) = Items[i];
            var off = VectorArt.Load(assets, values, icon);
            var image = new Image
            {
                Source = off, Width = 48, Height = 48, Stretch = Stretch.Uniform, Opacity = tab is null ? 0.35 : 1,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            var cell = new Border { Background = Brushes.Transparent, Child = image, [ColumnProperty] = i };
            if (tab is { } t)
            {
                _tabs.Add((t, image, off, VectorArt.Load(assets, values, icon + "_on") ?? off));
                Clicks.On(cell, () => Chosen?.Invoke(t));
            }
            Children.Add(cell);
        }
    }

    public event Action<AppTab>? Chosen;

    public void Show(AppTab selected)
    {
        foreach (var (tab, image, off, on) in _tabs) image.Source = tab == selected ? on : off;
    }
}
