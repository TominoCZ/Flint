using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Flint.App.Assets;
using Flint.App.Controls;
using Flint.App.Menu;
using Flint.Core.Model;

namespace Flint.App.Music;

/// <summary>
/// The Music tab, with only what drives the amp: its Looper section (<c>layout_looper_entry.xml</c>), whose Simple Looper
/// opens the looper page. The Groove Looper (drum tracks played from the phone) and the rest of the tab (songs, jam
/// tracks) are out of scope, so the Groove Looper is drawn dimmed.
/// </summary>
internal sealed class MusicScreen : PageStack
{
    public MusicScreen(MenuRows rows, AmpController amp, Action<string, Func<Task>> run) : base(rows, amp, run) =>
        Push(new TabPage(_ => "music", _ => Root()));

    private Control Root()
    {
        var title = Rows.Text(Rows.Values.Text("song_title"), 24, Brushes.White, semiBold: true);
        title.Margin = new Thickness(20, 0, 0, 0);
        var heading = Rows.Text(Rows.Values.Text("music_looper_title"), 16, Brushes.White, semiBold: true);
        heading.Margin = new Thickness(20, 40, 20, 16);
        var simple = Tile("icon_quick_looper", "music_looper_freemode");
        MenuRows.Tap(simple, () => Push(LooperPage.Create(this)));
        var groove = Tile("icon_creative_looper", "music_looper_drummachine");
        groove.Opacity = 0.35;
        groove.Margin = new Thickness(16, 0, 0, 0);
        groove[Grid.ColumnProperty] = 1;
        var tiles = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), Margin = new Thickness(20, 0), Children = { simple, groove } };
        var page = new StackPanel { Children = { new Grid { Height = 52, Children = { title } }, heading, tiles } };
        if (Rows.Assets.Image("bg_profile_header") is null) return page;
        var header = new Image { Source = Rows.Assets.Bitmap("bg_profile_header"), Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Top };
        return new Grid { Children = { header, page } };
    }

    /// <summary>A looper tile: its picture cropped from the right into the app's leaf shape, the name at its foot.</summary>
    private Control Tile(string picture, string name)
    {
        var label = Rows.Text(Rows.Values.Text(name), 14, new SolidColorBrush(Color.Parse("#f5f5f5")));
        (label.Margin, label.VerticalAlignment) = (new Thickness(8), VerticalAlignment.Bottom);
        var image = new Image
        {
            Source = Rows.Assets.Image(picture) is not null ? Rows.Assets.Bitmap(picture) : null, Stretch = Stretch.UniformToFill,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        return new Border
        {
            Height = 100, ClipToBounds = true, CornerRadius = new CornerRadius(40, 8, 40, 8), // JamEntryShapeImage
            Child = new Grid { Children = { image, label } },
        };
    }
}
