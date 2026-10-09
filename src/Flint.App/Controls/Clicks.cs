using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Flint.App.Controls;

/// <summary>
/// A left click on a control: pressed on it and released over it, with the hand cursor. Not Avalonia's Tapped: a quick
/// click where the last one landed counts as the second of a double click, and when it is on another control (the next
/// row after a scroll, the next page's row under the mouse) Avalonia raises neither Tapped nor DoubleTapped for it, so
/// the click was lost. A control inside another that takes clicks handles the click first. A border or panel without a
/// background only hits on its children, so it gets a transparent one: the whole of a button takes the click.
/// </summary>
internal static class Clicks
{
    public static void On(Control control, Action action)
    {
        var pressed = false;
        control.Cursor = new Cursor(StandardCursorType.Hand);
        if (control is Border { Background: null } border) border.Background = Brushes.Transparent;
        if (control is Panel { Background: null } panel) panel.Background = Brushes.Transparent;
        control.PointerPressed += (_, e) => pressed = e.GetCurrentPoint(control).Properties.IsLeftButtonPressed;
        control.PointerReleased += (_, e) =>
        {
            var click = pressed && !e.Handled && e.InitialPressMouseButton == MouseButton.Left && new Rect(control.Bounds.Size).Contains(e.GetPosition(control));
            pressed = false;
            if (!click) return;
            e.Handled = true;
            action();
        };
    }
}
