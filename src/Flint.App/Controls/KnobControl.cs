using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Flint.App.Assets;

namespace Flint.App.Controls;

/// <summary>
/// The app's knob (<c>com.positivegrid.controls.Knob</c>): the style's background with its indicator turned through the
/// style's sweep. Dragging up turns it up; a drag of three times its height covers the whole range, wherever it
/// started, and a gold glow sits behind it while held.
/// </summary>
internal sealed class KnobControl : Control
{
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<KnobControl, double>(nameof(Value));

    private const double DragRange = 3; // knob heights per full sweep, as Knob.onTouchEvent
    private readonly KnobArt _art;
    private readonly IBrush _glow;
    private (double Y, double Value)? _drag;

    static KnobControl() => AffectsRender<KnobControl>(ValueProperty);

    public KnobControl(KnobArt art, Color glowStart, Color glowEnd)
    {
        _art = art;
        _glow = new RadialGradientBrush
        {
            RadiusX = new RelativeScalar(0.8, RelativeUnit.Relative), RadiusY = new RelativeScalar(0.8, RelativeUnit.Relative),
            GradientStops = { new GradientStop(glowStart, 0), new GradientStop(glowEnd, 1) },
        };
        Cursor = new Cursor(StandardCursorType.SizeNorthSouth);
    }

    /// <summary>0 to 1, as the amp holds it.</summary>
    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, Math.Clamp(value, 0, 1));
    }

    /// <summary>Raised for every change the user makes by dragging (not for <see cref="Value"/> set from code).</summary>
    public event Action<double>? Turned;

    /// <summary>Raised when a drag starts (true) and ends (false), so the screen can show and hide the big value.</summary>
    public event Action<bool>? Held;

    public bool IsHeld => _drag is not null;

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(Brushes.Transparent, bounds); // grabbable in its corners too, where the art is transparent
        if (IsHeld) context.DrawEllipse(_glow, null, bounds);
        context.DrawImage(_art.Background, bounds);
        var angle = _art.FromDegrees + Value * (_art.ToDegrees - _art.FromDegrees);
        var centre = bounds.Center;
        using (context.PushTransform(Matrix.CreateTranslation(-centre.X, -centre.Y) * Matrix.CreateRotation(angle * Math.PI / 180)
                                     * Matrix.CreateTranslation(centre.X, centre.Y)))
            context.DrawImage(_art.Indicator, bounds);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        _drag = (e.GetPosition(this).Y, Value);
        e.Pointer.Capture(this);
        e.Handled = true;
        Held?.Invoke(true);
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_drag is not var (startY, startValue) || Bounds.Height <= 0) return;
        var value = Math.Clamp(startValue + (startY - e.GetPosition(this).Y) / (DragRange * Bounds.Height), 0, 1);
        if (value == Value) return;
        Value = value;
        Turned?.Invoke(value);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e) => EndDrag(e.Pointer);

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) => EndDrag(null);

    private void EndDrag(IPointer? pointer)
    {
        if (_drag is null) return;
        _drag = null;
        pointer?.Capture(null);
        Held?.Invoke(false);
        InvalidateVisual();
    }
}
