using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Flint.App.Assets;

namespace Flint.App.Tone;

/// <summary>
/// One device in the strip, drawn as <c>sigpath_tabview.xml</c> lays it out, in dp: 22 above and below,
/// a 20-high title under a 111-high column whose spacers weigh 2:7:0 when the device is on and 0:7:2 when off, so an
/// off device sits higher, darkened to 60% (its own pixels only). The selected one has a gold cell and title. Tap selects,
/// double-tap opens the model sheet, a vertical swipe switches it: down on, up off, as on the phone.
/// </summary>
internal sealed class ChainTab : Control
{
    public const double StripHeight = 175;
    private const double PadV = 22, PadH = 12, TitleHeight = 20, Column = StripHeight - 2 * PadV - TitleHeight, Inset = 2;
    private const double Cell = Column * 7 / 9, Lowered = Column * 2 / 9, SwipeDp = 24;
    private static readonly TimeSpan DoubleTapTime = TimeSpan.FromMilliseconds(300);
    private static readonly IBrush Gold = new SolidColorBrush(Color.Parse("#deb636")), TitleNormal = new SolidColorBrush(Color.Parse("#bebebe")),
        OffMask = new SolidColorBrush(Color.FromArgb(102, 0, 0, 0));

    private Bitmap? _icon;
    private bool _on, _selected;
    private Point? _pressedAt;
    private DateTime _lastTap;

    public ChainTab(string title, int slot)
    {
        Title = title;
        Slot = slot;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    public string Title { get; }

    /// <summary>The chain position, 0 (gate) to 6 (reverb).</summary>
    public int Slot { get; }

    public event Action<ChainTab>? Chosen, ModelsRequested;

    /// <summary>A vertical swipe: true to switch on (down), false off (up).</summary>
    public event Action<ChainTab, bool>? Swiped;

    public void Show(Bitmap? icon, bool on, bool selected)
    {
        if (ReferenceEquals(icon, _icon) && on == _on && selected == _selected) return;
        var resized = !ReferenceEquals(icon, _icon);
        (_icon, _on, _selected) = (icon, on, selected);
        if (resized) InvalidateMeasure();
        InvalidateVisual();
    }

    private double IconWidth => _icon is null ? 0 : (Cell - 2 * Inset) * _icon.PixelSize.Width / _icon.PixelSize.Height;

    protected override Size MeasureOverride(Size availableSize) => new(Math.Max(50, IconWidth + 2 * Inset) + 2 * PadH, StripHeight);

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size)); // the whole tab answers touches, as on the phone
        var cellWidth = Bounds.Width - 2 * PadH;
        var cell = new Rect(PadH, PadV + (_on ? Lowered : 0), cellWidth, Cell);
        if (_selected) context.DrawRectangle(Gold, null, cell, 4, 4); // sigpath_fx_selected_background: 4 dp corners
        if (_icon is not null)
        {
            var icon = new Rect(cell.Center.X - IconWidth / 2, cell.Y + Inset, IconWidth, Cell - 2 * Inset);
            context.DrawImage(_icon, icon);
            // Dim only the device's own pixels: its art is transparent around the body (between the jacks), and a
            // plain rectangle there darkens the gold of a selected cell, which the phone keeps bright.
            if (!_on)
                using (context.PushOpacityMask(new ImageBrush(_icon), icon))
                    context.FillRectangle(OffMask, icon);
        }
        var text = new FormattedText(Title.ToUpperInvariant(), CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(SparkFonts.Raleway, FontStyle.Normal, FontWeight.SemiBold), 14, _selected ? Gold : TitleNormal);
        context.DrawText(text, new Point((Bounds.Width - text.Width) / 2, StripHeight - PadV - TitleHeight + (TitleHeight - text.Height) / 2));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        _pressedAt = e.GetPosition(this);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_pressedAt is not { } from) return;
        _pressedAt = null;
        e.Pointer.Capture(null);
        var moved = e.GetPosition(this) - from;
        if (Math.Abs(moved.Y) >= SwipeDp && Math.Abs(moved.Y) > Math.Abs(moved.X))
        {
            Swiped?.Invoke(this, moved.Y > 0);
            return;
        }
        var now = DateTime.UtcNow;
        if (now - _lastTap < DoubleTapTime)
        {
            _lastTap = default;
            ModelsRequested?.Invoke(this);
            return;
        }
        _lastTap = now;
        Chosen?.Invoke(this);
    }
}
