using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Flint.App.Assets;
using Flint.App.Menu;
using Flint.App.Tone;
using Flint.Core.Model;

namespace Flint.App.Tuner;

/// <summary>
/// The tuner (<c>TunerFragment</c>, <c>fragment_tuner.xml</c>): the dial over the whole window and a close X at the top
/// right, which switches the amp's tuner off. Laid out in dp and scaled with the window, as the other screens are.
/// </summary>
internal sealed class TunerScreen : Grid
{
    private readonly TunerDial _dial;

    public TunerScreen(AssetStore assets, AndroidValues values)
    {
        Background = new SolidColorBrush(values.Color("grey950"));
        _dial = new TunerDial(values);
        var close = new Image
        {
            Source = VectorArt.Drawable(assets, values, "ic_cancel_close"), Width = 24, Height = 24, Margin = new Thickness(0, 10, 20, 0),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
        };
        MenuRows.Tap(close, () => Closed?.Invoke());
        var scale = new ScaleTransform(1, 1);
        Children.Add(new LayoutTransformControl { LayoutTransform = scale, Child = new Grid { Children = { _dial, close } } });
        SizeChanged += (_, e) => scale.ScaleX = scale.ScaleY = ToneScreen.Scale(e.NewSize.Height);
    }

    /// <summary>The X was pressed.</summary>
    public event Action? Closed;

    public void Show(AmpState? state)
    {
        if (!ReferenceEquals(_dial.Reading, state?.Tuning)) _dial.Reading = state?.Tuning;
    }
}
