using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Flint.App.Animation;
using Flint.App.Assets;
using Flint.App.Controls;
using Flint.Core.Connection;

namespace Flint.App.Connect;

/// <summary>
/// The app's connecting screen (<c>ConnectingFragment</c>, <c>content_connecting.xml</c>), shown until the
/// first sync: the <c>ripple_white_spin</c> rings at 10%, the Spark logo while searching for an amp not yet known or the
/// amp's picture while connecting to a known one, a 24 dp title, a 14 dp hint and the red rounded button. Laid out in
/// the phone's dp, where it sat on the owner's phone, and scaled with the window.
/// </summary>
internal sealed class ConnectScreen : Viewbox
{
    private const double DesignWidth = 360, DesignHeight = 785, RingsCentre = 285, Rings = 372;

    private readonly AssetStore _assets;
    private readonly AndroidValues _values;
    private readonly Image _picture = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock _title, _hint;
    private readonly Button _button;
    private readonly LottieView? _rings;
    private string? _pictureName;

    public ConnectScreen(AssetStore assets, AndroidValues values)
    {
        (_assets, _values) = (assets, values);
        Stretch = Stretch.Uniform;
        var canvas = new Canvas { Width = DesignWidth, Height = DesignHeight, ClipToBounds = false };
        if (assets.Raw("ripple_white_spin.json") is { } rings)
            Place(canvas, _rings = new LottieView(LottieAnimation.Load(rings)) { Width = Rings, Height = Rings, Opacity = 0.1 }, RingsCentre - Rings / 2, (DesignWidth - Rings) / 2);
        Place(canvas, new Border { Width = DesignWidth, Height = 180, Child = _picture }, RingsCentre - 90, 0);

        _title = new TextBlock { FontSize = 24, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White, TextAlignment = TextAlignment.Center, Width = DesignWidth };
        _hint = new TextBlock
        {
            FontSize = 14, FontFamily = SparkFonts.Roboto, Foreground = new SolidColorBrush(values.Color("grey500")),
            TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Width = DesignWidth - 64,
        };
        Place(canvas, _title, 458, 0);
        Place(canvas, _hint, 492, 32);

        _button = new Button
        {
            Width = DesignWidth - 112, Height = 48, CornerRadius = new CornerRadius(24), Background = new SolidColorBrush(values.Color("red500")),
            Foreground = Brushes.White, FontSize = 16, FontWeight = FontWeight.SemiBold,
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
        };
        _button.Click += (_, _) => ButtonPressed?.Invoke();
        Place(canvas, _button, 616, 56);
        Child = canvas;
    }

    /// <summary>The red button: Cancel while trying, Connect once cancelled.</summary>
    public event Action? ButtonPressed;

    /// <param name="searching">No amp is known yet, so the link scans for one (the app's "Searching..." with its logo).</param>
    public void Show(LinkState link, string? lastError, TimeSpan retryIn, bool searching)
    {
        string title, hint, picture;
        if (link == LinkState.Idle)
        {
            (title, hint, picture) = (Text("intro_title"), Text("intro_desc_connecting"), "spark_brand");
        }
        else if (link != LinkState.Backoff)
        {
            (title, picture) = searching ? (Text("connect_title_discovering"), "spark_brand") : (Text("connect_title_connecting"), "ic_menu_pager_device_2");
            hint = Text(searching ? "intro_desc_discovering" : "intro_desc_connecting");
        }
        else
        {
            var retry = $"Trying again in {Math.Max(1, Math.Ceiling(retryIn.TotalSeconds))} s.";
            (title, hint, picture) = lastError switch
            {
                { } e when e.Contains("Bluetooth is off") => (Text("intro_title_ble_off"), $"Turn Bluetooth on: Flint connects by itself. {retry}", "spark_brand"),
                { } e when e.Contains("not found", StringComparison.OrdinalIgnoreCase) =>
                    (Text("connect_title_not_found"), $"{Text("intro_desc_connecting")}, and that the phone is not connected to it. {retry}", "image_device_not_found_s_new"),
                _ => (Text("connect_title_connect_fail"), $"{lastError}. {retry}", searching ? "spark_brand" : "ic_menu_pager_device_2"),
            };
        }
        _title.Text = title;
        _hint.Text = hint;
        if (_rings is not null) _rings.Paused = link == LinkState.Idle; // once cancelled they slow to rest; Connect starts them again
        _button.Content = Text(link == LinkState.Idle ? "intro_connect" : "cancel");
        if (picture != _pictureName && _assets.Image(picture) is not null)
        {
            _pictureName = picture;
            _picture.Source = _assets.Bitmap(picture);
            _picture.Width = picture == "spark_brand" ? 148 : 232; // the layout's sparkLogo and ampImageView widths
        }
    }

    private string Text(string name) => _values.Text(name).Replace("\\'", "'");

    private static void Place(Canvas canvas, Control control, double top, double left)
    {
        Canvas.SetTop(control, top);
        Canvas.SetLeft(control, left);
        canvas.Children.Add(control);
    }
}
