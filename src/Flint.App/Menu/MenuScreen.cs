using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Flint.App.Controls;
using Flint.Core.Connection;
using Flint.Core.Model;

namespace Flint.App.Menu;

/// <summary>
/// The Menu tab (<c>fragment_menu.xml</c>): the amp's card, which opens its device page, and the Tuner row. The app's
/// other rows (profile, songs, shop, help) are out of scope.
/// </summary>
internal sealed class MenuScreen : PageStack
{
    /// <param name="openTuner">The Tuner row: the window opens the tuner over everything.</param>
    public MenuScreen(MenuRows rows, AmpController amp, Action<string, Func<Task>> run, Action openTuner) : base(rows, amp, run) =>
        Push(new TabPage(s => (s.Name, Amp.Link == LinkState.Ready), s => Root(s, openTuner)));

    private Control Root(AmpState state, Action openTuner)
    {
        var title = Rows.Text(Rows.Values.Text("profile_mainmenu"), 24, Brushes.White, semiBold: true);
        title.Margin = new Thickness(20, 0, 0, 0); // menu_toolbar_title_start_margin
        var top = new Grid { Height = 52, Children = { title } };
        var page = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        page.Children.Add(top);
        page.Children.Add(Scrolling(Card(state), Rows.Divider(),
            Rows.MenuItem("ic_menu_item_tuner", Rows.Values.Text("profile_tuner"), openTuner)));
        if (Rows.Assets.Image("bg_profile_header") is null) return page;
        // The chevrons behind the top (image_background, 600:338 of the width), under the page's own rows.
        var header = new Image { Source = Rows.Assets.Bitmap("bg_profile_header"), Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Top };
        return new Grid { Children = { header, page } };
    }

    /// <summary>The amp's card (<c>item_menu_pager_device.xml</c>): its picture, name and Tone Control state; a tap opens its device page.</summary>
    private Control Card(AmpState state)
    {
        var connected = Amp.Link == LinkState.Ready;
        var circles = Rows.Picture("ic_menu_pager_circles", 280);
        circles.VerticalAlignment = VerticalAlignment.Bottom;
        circles.HorizontalAlignment = HorizontalAlignment.Center;
        var device = Rows.Picture("ic_menu_pager_device_2");
        (device.Width, device.Height) = (160, 128);
        var name = Rows.Text(state.Name, 16, Brushes.White, semiBold: true, HorizontalAlignment.Center);
        name.Margin = new Thickness(12, 0);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        var link = Rows.Text(Rows.Values.Text("profile_mainmenu_ampconnectstatus_ToneControl"), 12, Brushes.White);
        link.Margin = new Thickness(12, 4, 4, 4);
        var linkIcon = Rows.Picture(connected ? "ic_menu_pager_tonecontrol_enabled" : "ic_menu_pager_tonecontrol_disabled");
        linkIcon.Margin = new Thickness(0, 4, 12, 4);
        var pill = new Border
        {
            Background = Rows.Colour("black30pa"), CornerRadius = new CornerRadius(16), HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 18), Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { link, linkIcon } },
        };
        var content = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 20, 0, 0),
            Children = { device, name, pill },
        };
        var card = new Border
        {
            Height = 266, Margin = new Thickness(22, 0, 22, 20), CornerRadius = new CornerRadius(16), ClipToBounds = true,
            Background = Rows.Colour("grey900"), Child = new Grid { Children = { circles, content } },
        };
        MenuRows.Tap(card, () => Push(DevicePage.Create(this)));
        return card;
    }
}
