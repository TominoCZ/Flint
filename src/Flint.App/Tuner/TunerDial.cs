using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Flint.App.Assets;
using Flint.Core.Protocol.Payloads;

namespace Flint.App.Tuner;

/// <summary>
/// The app's TunerView, drawn as it draws it, in dp: 21 ticks across the middle (the centre one tallest, the ±25 and ±50
/// ones taller, labelled), the tick under the note red, or the centre one green when in tune; the note above; and two
/// glowing arcs, ")(", that slide with the offset. The ticks open from the centre over a second, as on the phone. Where
/// the phone shows nothing or "Play a string..." depending on a 30 s timer, this shows "Play a string..." whenever
/// there is no signal.
/// <para>
/// Smoother than the phone: readings come about 16 times a second with some noise, so the dial glides to each one over
/// every screen frame instead of jumping, and a gap of "no signal" readings shorter than <see cref="Hold"/> (a fading
/// string) keeps the last note on screen. A new note, or a signal after silence, is shown at once.
/// </para>
/// </summary>
internal sealed class TunerDial : Control
{
    private const int Ticks = 21, Centre = 10;
    private static readonly int[] Labelled = [0, 5, 15, 20];
    private const double PhoneAspect = 9 / 20.0; // width over height of the owner's phone screen
    private static readonly TimeSpan Opening = TimeSpan.FromSeconds(1), Hold = TimeSpan.FromMilliseconds(400);

    // How quickly the dial follows a reading: a 63% step in this time. ponytail: one fixed time constant; if it still
    // feels laggy on big jumps or jittery when held, make it depend on the distance to go.
    private const double FollowSeconds = 0.08;

    private readonly Color _tick, _sharp, _tuned, _back, _glow;
    private readonly string _prompt;
    private readonly DateTime _opened = DateTime.UtcNow;
    private readonly DispatcherTimer _frames = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private TunerReading? _reading, _signal; // the last reading, and the last one with a note
    private DateTime _signalAt;
    private double _offset; // where the dial is drawn, gliding towards _signal's offset
    private TimeSpan? _lastFrame;
    private bool _following;

    public TunerDial(AndroidValues values)
    {
        (_tick, _sharp, _tuned, _back, _glow) = (values.Color("white50pa"), values.Color("red700"), values.Color("green300"),
            values.Color("grey950"), values.Color("tuner_ball_gradient"));
        _prompt = values.Text("tuner_start");
        _frames.Tick += (_, _) =>
        {
            if (DateTime.UtcNow - _opened > Opening) _frames.Stop();
            InvalidateVisual();
        };
        AttachedToVisualTree += (_, _) => _frames.Start();
        DetachedFromVisualTree += (_, _) => _frames.Stop();
    }

    /// <summary>The reading to show, or null before the first.</summary>
    public TunerReading? Reading
    {
        get => _reading;
        set
        {
            _reading = value;
            if (value is { HasSignal: true })
            {
                var heldNote = _signal is { } last && DateTime.UtcNow - _signalAt <= Hold && last.Note == value.Note;
                if (!heldNote) _offset = value.Offset; // a new note or a fresh signal: no sweep from the old place
                (_signal, _signalAt) = (value, DateTime.UtcNow);
                Follow();
            }
            InvalidateVisual();
        }
    }

    /// <summary>Where the dial is drawn, 0 to 1: it glides towards the reading's offset.</summary>
    public double ShownOffset => _offset;

    private void Follow()
    {
        if (_following || TopLevel.GetTopLevel(this) is not { } top) return;
        _following = true;
        top.RequestAnimationFrame(Frame);
    }

    private void Frame(TimeSpan now)
    {
        var seconds = _lastFrame is { } last ? Math.Clamp((now - last).TotalSeconds, 0, 0.1) : 1 / 60.0;
        _lastFrame = now;
        var target = _signal?.Offset ?? _offset;
        _offset += (target - _offset) * (1 - Math.Exp(-seconds / FollowSeconds));
        InvalidateVisual();
        if (Math.Abs(target - _offset) > 0.0005 && TopLevel.GetTopLevel(this) is { } top)
        {
            top.RequestAnimationFrame(Frame);
            return;
        }
        _offset = target;
        (_following, _lastFrame) = (false, null);
    }

    public override void Render(DrawingContext context)
    {
        var (centreX, centreY) = (Bounds.Width / 2, Bounds.Height / 2);
        var width = Bounds.Width * (Bounds.Width > Bounds.Height ? 0.74 : 0.9); // the phone's 0.9, a landscape tablet's 0.74
        var spacing = width / Ticks;
        var opened = (DateTime.UtcNow - _opened) / Opening;
        // The note while there is a signal, or was one moments ago; its place on the dial is the gliding offset.
        var signal = _reading is { HasSignal: true } || DateTime.UtcNow - _signalAt <= Hold ? _signal : null;
        var current = signal is null ? -1 : signal.InTune ? Centre : Math.Clamp((int)Math.Floor(_offset * Ticks), 0, Ticks - 1);

        using (context.PushTransform(Matrix.CreateTranslation(centreX, centreY)))
        {
            for (var i = 0; i < Ticks; i++)
            {
                if (Math.Abs(i - Centre) > opened * Centre) continue;
                var (w, h) = i == Centre ? (6.0, 32.0) : Labelled.Contains(i) ? (4.0, 20.0) : (4.0, 12.0);
                var colour = i != current ? _tick : current == Centre ? _tuned : _sharp;
                var x = (i - Centre) * spacing;
                // The app asks for an 8 dp radius; Android's Skia scales both radii down together until they fit, which makes
                // a capsule with round ends. Avalonia clamps each on its own, which would make an oval.
                var radius = 8 * Math.Min(1, Math.Min(w, h) / 16);
                context.DrawRectangle(new SolidColorBrush(colour), null, new Rect(x - w / 2, -h / 2, w, h), radius, radius);
                if (Labelled.Contains(i))
                {
                    var label = Text(((i - Centre) * 5).ToString(CultureInfo.InvariantCulture), 8, Colors.White);
                    context.DrawText(label, new Point(x - label.Width / 2, h / 2 + 20 - label.Baseline));
                }
            }
            if (opened >= 1)
            {
                var note = signal is null ? Text(_prompt, 24, Colors.White) : Text(signal.Name, 80, signal.InTune ? _tuned : Colors.White);
                context.DrawText(note, new Point(-note.Width / 2, -64 - note.Baseline));
            }
            if (signal is not null) DrawGlow(context, (_offset - 0.5) * width, signal.InTune ? 0.06 : 0.2);
        }
    }

    /// <summary>
    /// The ")(" arcs: the right half of a huge circle left of the centre and the left half of one right of it, each
    /// filled with a radial gradient that is a thin bright ring near its edge, shifted by the offset.
    /// </summary>
    private void DrawGlow(DrawingContext context, double shift, double alpha)
    {
        // The app sizes the circles by the screen's width (1.5×, the ring 0.07 of the 0.9-wide dial), which on a phone
        // stands upright. A wide window would make them so big that only their straight tips show, with a blurred ring,
        // so they are sized for a phone-shaped screen as tall as this one; a narrow window is the phone case exactly.
        var phoneWidth = Math.Min(Bounds.Width, Bounds.Height * PhoneAspect);
        var radius = phoneWidth * 1.5;
        var reach = phoneWidth * 0.9 * 0.07;
        foreach (var side in new[] { -1, 1 })
        {
            var centre = new Point(side * radius + shift, 0);
            var brush = new RadialGradientBrush
            {
                Center = new RelativePoint(centre, RelativeUnit.Absolute), GradientOrigin = new RelativePoint(centre, RelativeUnit.Absolute),
                RadiusX = new RelativeScalar(radius + reach, RelativeUnit.Absolute), RadiusY = new RelativeScalar(radius + reach, RelativeUnit.Absolute),
                Opacity = alpha,
                GradientStops =
                {
                    new GradientStop(_back, 0.95), new GradientStop(_glow, 0.95125), new GradientStop(Colors.White, 0.95475),
                    new GradientStop(_tick, 0.97625), new GradientStop(_back, 1.0),
                },
            };
            // The half of the circle that faces the centre, reaching 2 × reach past it.
            var edge = centre.X - side * (radius + 2 * reach);
            var half = new StreamGeometry();
            using (var g = half.Open())
            {
                g.BeginFigure(new Point(centre.X, -radius), true);
                g.ArcTo(new Point(centre.X, radius), new Size(Math.Abs(edge - centre.X), radius), 0, false,
                    side < 0 ? SweepDirection.Clockwise : SweepDirection.CounterClockwise);
                g.EndFigure(true);
            }
            context.DrawGeometry(brush, null, half);
        }
    }

    private static FormattedText Text(string text, double size, Color colour) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(SparkFonts.Roboto), size, new SolidColorBrush(colour));
}
