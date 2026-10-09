using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Flint.App.Assets;
using Flint.App.Controls;

namespace Flint.App.Menu;

/// <summary>
/// The rows the Menu tab's pages are made of, in dp with the app's dimensions, colours and fonts: the device page's
/// info rows (<c>item_device_detail_info.xml</c>), its status and switch rows, the settings pickers' rows
/// (<c>item_device_preference.xml</c>, <c>_hint</c>), dividers (<c>item_divider.xml</c>) and the top bars.
/// </summary>
internal sealed class MenuRows(AssetStore assets, AndroidValues values)
{
    private const double SideMargin = 20, RowMargin = 14; // menu_device_detail_item_horizontal_margin, _vertical_margin

    public AssetStore Assets => assets;
    public AndroidValues Values => values;

    /// <summary>A page's top bar: back on the left and the title in the middle (<c>layout_preference_top_bar.xml</c>, 44 dp).</summary>
    public Control TopBar(string? title, Action back, double height = 44, double backMargin = 16)
    {
        var bar = new Grid { Height = height };
        if (title is not null)
            bar.Children.Add(Text(title, 20, Brushes.White, semiBold: true, HorizontalAlignment.Center));
        var arrow = Picture("ic_cancel_back", 24);
        arrow.HorizontalAlignment = HorizontalAlignment.Left;
        arrow.Margin = new Thickness(backMargin, 0, 0, 0);
        Tap(arrow, back);
        bar.Children.Add(arrow);
        return bar;
    }

    /// <summary>A title with its value on the right, an optional second line under the title, and an arrow if it opens something.</summary>
    public Control Info(string title, string? value, string? second = null, Action? open = null)
    {
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(SideMargin, RowMargin, 0, RowMargin) };
        left.Children.Add(Text(title, 16, Brushes.White, semiBold: true));
        if (second is not null) left.Children.Add(Text(second, 14, Colour("white50pa")));
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Background = Brushes.Transparent };
        row.Children.Add(left);
        var description = Text(value ?? "", 14, Colour("white50pa"), align: HorizontalAlignment.Right);
        description.TextTrimming = TextTrimming.CharacterEllipsis;
        description.Margin = new Thickness(12, 0, open is null ? SideMargin : 0, 0);
        description[Grid.ColumnProperty] = 1;
        row.Children.Add(description);
        if (open is not null)
        {
            var arrow = Picture("ic_arrow_right");
            arrow.Margin = new Thickness(10, 0, SideMargin, 0);
            arrow[Grid.ColumnProperty] = 2;
            row.Children.Add(arrow);
            Tap(row, open);
        }
        return row;
    }

    /// <summary>A link's state: title, "Connected" under it, and the green tick while it is (the device page's first rows).</summary>
    public Control Status(string title, string state, bool ok)
    {
        var tick = Picture("ic_check_device_detail", 20);
        tick.IsVisible = ok;
        tick.HorizontalAlignment = HorizontalAlignment.Right;
        tick.Margin = new Thickness(0, 0, SideMargin, 0);
        return new Grid { Children = { TitleAndDescription(title, state), tick } };
    }

    /// <summary>A setting with a description and the app's red switch (the device page's Line Out Mute Behavior).</summary>
    public Control Switch(string title, string description, bool on, Action toggle)
    {
        var knob = new Ellipse { Width = 20, Height = 20, Fill = Brushes.White, Margin = new Thickness(2), HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left };
        var pill = new Border
        {
            Width = 44, Height = 24, CornerRadius = new CornerRadius(12), Child = knob, VerticalAlignment = VerticalAlignment.Center,
            Background = on ? new SolidColorBrush(values.Color("spark_red_500")) : Colour("white30pa"),
        };
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"), Background = Brushes.Transparent,
            Children = { TitleAndDescription(title, description), new Border { Child = pill, Margin = new Thickness(16, 0, SideMargin, 0), [Grid.ColumnProperty] = 1 } },
        };
        Tap(row, toggle);
        return row;
    }

    /// <summary>One choice in a settings picker: its name, the tick when chosen, greyed when the other setting rules it out.</summary>
    public Control Choice(string title, bool chosen, bool enabled, Action choose)
    {
        var tick = Picture("icon_check", 20);
        tick.IsVisible = chosen;
        tick.HorizontalAlignment = HorizontalAlignment.Right;
        var row = new Grid
        {
            Background = Brushes.Transparent, Margin = new Thickness(SideMargin, RowMargin),
            Children = { Text(title, 16, enabled ? Brushes.White : Colour("white20pa"), semiBold: true), tick },
        };
        if (enabled) Tap(row, choose);
        return row;
    }

    /// <summary>The grey note under a picker's choices.</summary>
    public Control Hint(string text)
    {
        var hint = Text(text, 14, Colour("white30pa"));
        hint.Margin = new Thickness(SideMargin, RowMargin);
        hint.TextWrapping = TextWrapping.Wrap;
        return hint;
    }

    /// <summary>A Menu tab row (<c>item_menu_view.xml</c>): icon, title and arrow.</summary>
    public Control MenuItem(string icon, string title, Action open)
    {
        var image = Picture(icon);
        image.Margin = new Thickness(SideMargin, 0, 16, 0);
        var label = Text(title, 16, Brushes.White, semiBold: true);
        label[Grid.ColumnProperty] = 1;
        var arrow = Picture("ic_menu_item_arrow_right");
        arrow.Margin = new Thickness(SideMargin, 0, 14, 0);
        arrow[Grid.ColumnProperty] = 2;
        var row = new Grid { Height = 56, ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Background = Brushes.Transparent, Children = { image, label, arrow } };
        Tap(row, open);
        return row;
    }

    public Control Divider() => new Border { Height = 1, Margin = new Thickness(0, 11), Background = Colour("white06pa") };

    /// <summary>A drawable at its own size in dp, or at <paramref name="size"/> dp square.</summary>
    public Image Picture(string drawable, double size = double.NaN) => VectorArt.Picture(assets, values, drawable, size);

    public TextBlock Text(string text, double size, IBrush colour, bool semiBold = false, HorizontalAlignment align = HorizontalAlignment.Left) => new()
    {
        Text = text, FontSize = size, Foreground = colour, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = align,
        FontFamily = semiBold ? SparkFonts.Raleway : SparkFonts.Roboto, FontWeight = semiBold ? FontWeight.SemiBold : FontWeight.Normal,
    };

    public IBrush Colour(string name) => new SolidColorBrush(values.Color(name));

    /// <summary>Makes a control a button: a hand over it, and <paramref name="action"/> on a click.</summary>
    public static void Tap(Control control, Action action) => Clicks.On(control, action);

    private StackPanel TitleAndDescription(string title, string description)
    {
        var detail = Text(description, 14, Colour("white50pa"));
        detail.TextWrapping = TextWrapping.Wrap;
        return new StackPanel
        {
            Margin = new Thickness(SideMargin, RowMargin, 0, RowMargin), Spacing = 4, VerticalAlignment = VerticalAlignment.Center,
            Children = { Text(title, 16, Brushes.White, semiBold: true), detail },
        };
    }
}
