using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Flint.App.Controls;

/// <summary>
/// A menu dropped down from the control that opened it, instead of the phone's full-screen sheet: the
/// card hangs under its anchor, flush with its left or right edge, scaled with the screen; a click anywhere else
/// closes it.
/// </summary>
internal sealed class Dropdown : Grid
{
    /// <param name="anchor">The opening control.</param>
    /// <param name="within">The screen the dropdown covers, which <paramref name="anchor"/> is inside.</param>
    public Dropdown(Control card, Control anchor, Control within, ScaleTransform scale, bool alignRight)
    {
        Background = Brushes.Transparent; // takes the clicks beside the card
        var topLeft = anchor.TranslatePoint(default, within) ?? default;
        var bottomRight = anchor.TranslatePoint(new Point(anchor.Bounds.Width, anchor.Bounds.Height), within) ?? default;
        Children.Add(new LayoutTransformControl
        {
            LayoutTransform = scale, VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = alignRight ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Margin = alignRight ? new Thickness(0, bottomRight.Y, within.Bounds.Width - bottomRight.X, 0) : new Thickness(topLeft.X, bottomRight.Y, 0, 0),
            Child = new Border
            {
                CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = card,
                BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 4, Blur = 16, Color = Color.Parse("#99000000") }),
            },
        });
        PointerPressed += (_, e) =>
        {
            if (e.Source == this) Closed?.Invoke();
        };
    }

    /// <summary>The space under the anchor down to the foot of the screen, in the card's own (unscaled) units.</summary>
    public static double RoomBelow(Control anchor, Control within, ScaleTransform scale) =>
        (within.Bounds.Height - (anchor.TranslatePoint(new Point(0, anchor.Bounds.Height), within)?.Y ?? 0) - 16) / scale.ScaleY;

    /// <summary>A click beside the card.</summary>
    public event Action? Closed;
}
