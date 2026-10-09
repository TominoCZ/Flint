using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Flint.App.Assets;

namespace Flint.App.Music;

/// <summary>
/// The looper settings' tempo wheel (<c>CircularSlider</c> on <c>bg_bpm_pad_240</c>, 240 dp): turning it round changes the
/// tempo by one BPM every 9° (40 to 300 over 2340°), the dot at its top turning with it; the round button in the middle
/// (<c>bg_bpm_tap_240</c>) shows the tempo and sets it by tapping. The mouse wheel steps it too. A change is reported
/// when the drag ends, so the amp gets one write per gesture.
/// </summary>
internal sealed class BpmWheel : Control
{
    private const double DegreesPerBpm = 9, TapSize = 168;

    private readonly IImage? _pad, _mask, _tap, _tapPressed;
    private readonly IBrush _dotEdge, _label;
    private readonly string _bpmText;
    private readonly List<DateTime> _taps = [];
    private Point? _dragFrom;
    private double _carry;
    private bool _tapHeld;

    public BpmWheel(AssetStore assets, AndroidValues values, int bpm, int min, int max)
    {
        (Width, Height, Min, Max, Bpm) = (240, 240, min, max, Math.Clamp(bpm, min, max));
        IImage? Bitmap(string name) => assets.Image(name) is not null ? assets.Bitmap(name) : null;
        (_pad, _mask, _tap, _tapPressed) = (Bitmap("bg_bpm_slider_240"), Bitmap("bg_bpm_slider_mask_240"), Bitmap("bg_bpm_tap_normal_240"), Bitmap("bg_bpm_tap_pressed_240"));
        (_dotEdge, _label, _bpmText) = (new SolidColorBrush(values.Color("grey700")), new SolidColorBrush(values.Color("grey500")), values.Text("bpm"));
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    public int Min { get; }
    public int Max { get; }
    public int Bpm { get; private set; }

    /// <summary>A gesture ended on a new tempo: a drag let go, a tap or a mouse-wheel step.</summary>
    public event Action<int>? Changed;

    public override void Render(DrawingContext context)
    {
        var box = new Rect(Bounds.Size);
        var centre = box.Center;
        context.FillRectangle(Brushes.Transparent, box); // the whole wheel takes the mouse
        using (context.PushTransform(Matrix.CreateTranslation(-centre.X, -centre.Y) * Matrix.CreateRotation((Bpm - Min) * DegreesPerBpm * Math.PI / 180)
                                     * Matrix.CreateTranslation(centre.X, centre.Y)))
        {
            if (_pad is not null) context.DrawImage(_pad, box);
            context.DrawEllipse(Brushes.White, new Pen(_dotEdge, 10), new Point(centre.X, 2 + 16), 11, 11); // 32 dp across its 10 dp edge
        }
        if (_mask is not null) context.DrawImage(_mask, box);
        var tap = new Rect(centre.X - TapSize / 2, centre.Y - TapSize / 2, TapSize, TapSize);
        if ((_tapHeld ? _tapPressed : _tap) is { } art) context.DrawImage(art, tap);
        var number = Text(Bpm.ToString(CultureInfo.InvariantCulture), 56, Brushes.White, FontWeight.Light);
        context.DrawText(number, new Point(centre.X - number.Width / 2, tap.Y + (TapSize - 20 - number.Height) / 2));
        var label = Text(_bpmText, 14, _label, FontWeight.SemiBold);
        context.DrawText(label, new Point(centre.X - label.Width / 2, tap.Bottom - 40 - label.Height));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var at = e.GetPosition(this);
        if (Distance(at) <= TapSize / 2) _tapHeld = true;
        else (_dragFrom, _carry) = (at, 0);
        e.Pointer.Capture(this);
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_dragFrom is not { } from) return;
        var to = e.GetPosition(this);
        _dragFrom = to;
        var turned = Angle(to) - Angle(from);
        _carry += turned > 180 ? turned - 360 : turned < -180 ? turned + 360 : turned; // a slow turn still adds up
        var steps = (int)(_carry / DegreesPerBpm);
        if (steps == 0) return;
        _carry -= steps * DegreesPerBpm;
        Bpm = Math.Clamp(Bpm + steps, Min, Max);
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_tapHeld && Distance(e.GetPosition(this)) <= TapSize / 2) TapTempo();
        else if (_dragFrom is not null) Changed?.Invoke(Bpm);
        (_tapHeld, _dragFrom) = (false, null);
        e.Pointer.Capture(null);
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        var next = Math.Clamp(Bpm + Math.Sign(e.Delta.Y), Min, Max);
        if (next == Bpm) return;
        Bpm = next;
        InvalidateVisual();
        Changed?.Invoke(Bpm);
        e.Handled = true;
    }

    /// <summary>Tap tempo: the average gap between the last taps, starting again after a 2 s pause.</summary>
    private void TapTempo()
    {
        var now = DateTime.UtcNow;
        if (_taps.Count > 0 && now - _taps[^1] > TimeSpan.FromSeconds(2)) _taps.Clear();
        _taps.Add(now);
        if (_taps.Count > 4) _taps.RemoveAt(0);
        if (_taps.Count < 2) return;
        var gap = (_taps[^1] - _taps[0]).TotalSeconds / (_taps.Count - 1);
        Bpm = Math.Clamp((int)Math.Round(60 / gap), Min, Max);
        Changed?.Invoke(Bpm);
    }

    private double Distance(Point at)
    {
        var centre = new Rect(Bounds.Size).Center;
        return Math.Sqrt(Math.Pow(at.X - centre.X, 2) + Math.Pow(at.Y - centre.Y, 2));
    }

    /// <summary>Degrees clockwise from 12 o'clock.</summary>
    private double Angle(Point at)
    {
        var centre = new Rect(Bounds.Size).Center;
        return Math.Atan2(at.X - centre.X, centre.Y - at.Y) * 180 / Math.PI;
    }

    private static FormattedText Text(string text, double size, IBrush colour, FontWeight weight) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(SparkFonts.Raleway, FontStyle.Normal, weight), size, colour);
}
