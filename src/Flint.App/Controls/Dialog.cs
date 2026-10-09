using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Flint.App.Menu;

namespace Flint.App.Controls;

/// <summary>
/// The app's question popup (<c>popup_two_buttons_horizontal.xml</c>): a light card with a title, a line of text, a red
/// text button to back out and a red button to go ahead, over a dimmed screen.
/// </summary>
internal sealed class Dialog : Grid
{
    /// <param name="answered">True for the red button, false for the other or a click beside the card.</param>
    public Dialog(MenuRows rows, string title, string text, string cancel, string confirm, Action<bool> answered)
    {
        Background = new SolidColorBrush(Color.Parse("#99000000"));
        var heading = rows.Text(title, 18, rows.Colour("grey950"), semiBold: true, HorizontalAlignment.Center);
        heading.Margin = new Thickness(24, 0, 24, 16);
        var body = rows.Text(text, 14, rows.Colour("black70pa"), align: HorizontalAlignment.Center);
        (body.TextWrapping, body.TextAlignment, body.Margin) = (TextWrapping.Wrap, TextAlignment.Center, new Thickness(24, 0, 24, 32));
        var no = Button(cancel, null, rows.Colour("red500"));
        var yes = Button(confirm, rows.Colour("red500"), Brushes.White);
        no.Margin = new Thickness(16, 0, 4, 0);
        yes.Margin = new Thickness(4, 0, 16, 0);
        yes[ColumnProperty] = 1;
        Clicks.On(no, () => answered(false));
        Clicks.On(yes, () => answered(true));
        Children.Add(new Border
        {
            Background = rows.Colour("grey100"), CornerRadius = new CornerRadius(24), Margin = new Thickness(24), Padding = new Thickness(0, 32, 0, 16),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel { Children = { heading, body, new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), Children = { no, yes } } } },
        });
        PointerPressed += (_, e) =>
        {
            if (e.Source == this) answered(false); // beside the card
        };

        Border Button(string label, IBrush? fill, IBrush colour) => new()
        {
            Height = 44, CornerRadius = new CornerRadius(22), Background = fill ?? Brushes.Transparent,
            Child = rows.Text(label, 14, colour, semiBold: true, HorizontalAlignment.Center),
        };
    }
}
