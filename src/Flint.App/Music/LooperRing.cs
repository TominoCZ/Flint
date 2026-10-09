using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Flint.App.Music;

/// <summary>
/// The Simple Looper's ring (<c>visualized_loop</c>, 192 dp): the app's ring art for the loop's length under it, and the
/// progress drawn over it as the app draws it, one slice per beat when the click is on (or one sweep when it is off),
/// done beats in the state's colour and the current one brighter.
/// </summary>
internal sealed class LooperRing : Control
{
    private IImage? _art;
    private (int Beats, double Progress, Color Colour) _shown;

    /// <summary>The ring art (<c>bg_looper_4_04</c>, <c>bg_looper_free</c>).</summary>
    public IImage? Art
    {
        get => _art;
        set
        {
            _art = value;
            InvalidateVisual();
        }
    }

    /// <param name="beats">Slices round the ring: beats × bars, or 0 for one continuous sweep.</param>
    /// <param name="progress">How far round, 0 to 1.</param>
    public void Show(int beats, double progress, Color colour)
    {
        var shown = (beats, Math.Clamp(progress, 0, 1), colour);
        if (shown == _shown) return;
        _shown = shown;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var box = new Rect(Bounds.Size);
        if (_art is not null) context.DrawImage(_art, box);
        var (beats, progress, colour) = _shown;
        if (progress <= 0) return;
        var inset = box.Width * 0.0075; // the app's 0.015 × ½ of its 600 px canvas
        var area = box.Deflate(inset);
        if (beats <= 0)
        {
            Slice(context, area, 0, progress * 360, colour, 1);
            return;
        }
        var step = 360.0 / beats;
        var current = (int)Math.Ceiling(progress * beats);
        for (var i = 1; i < current; i++) Slice(context, area, (i - 1) * step, step, colour, 0.75);
        Slice(context, area, (current - 1) * step, step, colour, 1);
    }

    /// <summary>A pie slice from 12 o'clock, filled dark at the centre and bright at the rim, as the app's gradients are.</summary>
    private static void Slice(DrawingContext context, Rect area, double from, double sweep, Color colour, double strength)
    {
        if (sweep <= 0) return;
        var centre = area.Center;
        var radius = area.Width / 2;
        Point At(double degrees) => new(centre.X + radius * Math.Sin(degrees * Math.PI / 180), centre.Y - radius * Math.Cos(degrees * Math.PI / 180));
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(centre, true);
            g.LineTo(At(from));
            g.ArcTo(At(from + Math.Min(sweep, 359.99)), new Size(radius, radius), 0, sweep > 180, SweepDirection.Clockwise);
            g.EndFigure(true);
        }
        var brush = new RadialGradientBrush
        {
            Center = new RelativePoint(centre, RelativeUnit.Absolute), GradientOrigin = new RelativePoint(centre, RelativeUnit.Absolute),
            RadiusX = new RelativeScalar(radius, RelativeUnit.Absolute), RadiusY = new RelativeScalar(radius, RelativeUnit.Absolute),
            Opacity = strength,
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0, colour.R, colour.G, colour.B), 0.4), new GradientStop(Color.FromArgb(90, colour.R, colour.G, colour.B), 0.8),
                new GradientStop(colour, 0.8), new GradientStop(colour, 1),
            },
        };
        context.DrawGeometry(brush, null, geometry);
    }
}
