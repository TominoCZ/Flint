using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Flint.App.Controls;

/// <summary>
/// The app's <c>Slider</c> (the EQs' faders), as its <c>onMeasure</c> and <c>onTouchEvent</c> work: the
/// shadow is the slider's width and width ÷ <c>thumb_shadow_width_height_ratio</c> tall, the thumb
/// <c>thumb_to_shadow_width_ratio</c> of that wide; the thumb's centre travels a band <c>draggable_height_ratio</c> of
/// the height, top = 1. A drag starts only on the thumb and moves its centre to the pointer; a double-tap resets to 0.5.
/// </summary>
internal sealed class SliderControl : Control
{
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<SliderControl, double>(nameof(Value));

    private const double Default = 0.5;
    private static readonly TimeSpan DoubleTap = TimeSpan.FromMilliseconds(300);
    private readonly Bitmap? _thumb, _shadow;
    private readonly double _draggable, _shadowRatio, _thumbToShadow, _thumbRatio;
    private bool _dragging;
    private DateTime _lastDown;

    static SliderControl() => AffectsRender<SliderControl>(ValueProperty);

    /// <param name="style">The slider's attributes with its style merged: thumb, thumb_shadow and the four ratios.</param>
    public SliderControl(IReadOnlyDictionary<string, string> style, Func<string, Bitmap?> image)
    {
        double Ratio(string name, double fallback) =>
            style.TryGetValue(name, out var v) && double.TryParse(v, CultureInfo.InvariantCulture, out var d) ? d : fallback;
        _thumb = style.TryGetValue("thumb", out var t) ? image(t.Split('/').Last()) : null;
        _shadow = style.TryGetValue("thumb_shadow", out var s) ? image(s.Split('/').Last()) : null;
        (_draggable, _shadowRatio, _thumbToShadow, _thumbRatio) = (Ratio("draggable_height_ratio", 0.7),
            Ratio("thumb_shadow_width_height_ratio", 0.68), Ratio("thumb_to_shadow_width_ratio", 0.42), Ratio("thumb_width_height_ratio", 0.42));
        Cursor = new Cursor(StandardCursorType.SizeNorthSouth);
    }

    /// <summary>0 at the bottom to 1 at the top, as the amp holds it.</summary>
    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, Math.Clamp(value, 0, 1));
    }

    public bool IsHeld => _dragging;

    /// <summary>A change the user made (drag or double-tap), not one set from code.</summary>
    public event Action<double>? Moved;

    public event Action<bool>? Held;

    private double ThumbWidth => Bounds.Width * _thumbToShadow;
    private double ThumbHeight => ThumbWidth / _thumbRatio;
    private double Band => Bounds.Height * _draggable;
    private double Top => ThumbHeight / 2;

    /// <summary>The thumb's top edge for the current value (the shadow shares it).</summary>
    private double ThumbY => Top + Band * (1 - Value) - ThumbHeight / 2;

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        var y = ThumbY;
        if (_shadow is not null) context.DrawImage(_shadow, new Rect(0, y, Bounds.Width, Bounds.Width / _shadowRatio));
        if (_thumb is not null) context.DrawImage(_thumb, new Rect((Bounds.Width - ThumbWidth) / 2, y, ThumbWidth, ThumbHeight));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var now = DateTime.UtcNow;
        if (now - _lastDown <= DoubleTap)
        {
            _lastDown = default;
            Value = Default;
            Moved?.Invoke(Default);
            e.Handled = true;
            return;
        }
        var y = e.GetPosition(this).Y;
        if (y < ThumbY || y > ThumbY + ThumbHeight) return; // only the thumb is grabbed
        _lastDown = now;
        _dragging = true;
        e.Pointer.Capture(this);
        e.Handled = true;
        Held?.Invoke(true);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!_dragging || Band <= 0) return;
        var y = Math.Clamp(e.GetPosition(this).Y, Top, Top + Band);
        var value = 1 - (y - Top) / Band;
        if (value == Value) return;
        Value = value;
        Moved?.Invoke(value);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e) => End(e.Pointer);

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) => End(null);

    private void End(IPointer? pointer)
    {
        if (!_dragging) return;
        _dragging = false;
        pointer?.Capture(null);
        Held?.Invoke(false);
    }
}
