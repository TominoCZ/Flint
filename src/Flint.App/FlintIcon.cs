using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Flint.Core.Library;
using Flint.Core.Logging;

namespace Flint.App;

/// <summary>
/// Flint's own icon, drawn here rather than shipped as a file: a small combo amp, with a handle, a gold control panel
/// of knobs and a jewel light, and a grille of crossed mesh below. Not the Spark app's: Flint is not Positive Grid's. The
/// window and the tray take the bitmap; the launcher and login entries take <see cref="Save"/>'s PNG.
/// </summary>
internal static class FlintIcon
{
    private static readonly IBrush Tolex = Vertical("#3a3a40", "#1c1c20"), Panel = Vertical("#f0cf86", "#c99a45"), Knob = new SolidColorBrush(Color.Parse("#26262a")),
                                   Jewel = new SolidColorBrush(Color.Parse("#ff5a2a")), Grille = new SolidColorBrush(Color.Parse("#151518")),
                                   Mesh = new SolidColorBrush(Color.Parse("#3a3a41"));

    /// <summary>The icon at <paramref name="size"/> pixels square.</summary>
    public static Bitmap Render(int size)
    {
        var bitmap = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
        using (var context = bitmap.CreateDrawingContext())
        {
            var s = (double)size;
            Rect Box(double left, double top, double right, double bottom) => new(left * s, top * s, (right - left) * s, (bottom - top) * s);
            context.DrawRectangle(null, new Pen(Tolex, s * 0.05), new RoundedRect(Box(0.35, 0.07, 0.65, 0.2), s * 0.04)); // the handle
            context.DrawRectangle(Tolex, null, new RoundedRect(Box(0.06, 0.16, 0.94, 0.92), s * 0.08));
            context.DrawRectangle(Panel, null, new RoundedRect(Box(0.11, 0.22, 0.89, 0.36), s * 0.03));
            foreach (var x in new[] { 0.22, 0.37, 0.52, 0.67 }) context.DrawEllipse(Knob, null, new Point(x * s, 0.29 * s), s * 0.04, s * 0.04);
            context.DrawEllipse(Jewel, null, new Point(0.81 * s, 0.29 * s), s * 0.03, s * 0.03);
            var grille = new RoundedRect(Box(0.11, 0.41, 0.89, 0.87), s * 0.04);
            context.DrawRectangle(Grille, null, grille);
            using (context.PushClip(grille)) // the cloth: two sets of diagonal threads
            {
                var thread = new Pen(Mesh, Math.Max(1, s * 0.014));
                for (var d = -s; d < s; d += s * 0.055)
                {
                    context.DrawLine(thread, new Point(d, 0.41 * s), new Point(d + 0.46 * s, 0.87 * s));
                    context.DrawLine(thread, new Point(d + 0.46 * s, 0.41 * s), new Point(d, 0.87 * s));
                }
            }
        }
        return bitmap;
    }

    /// <summary>
    /// Writes the icon as a PNG into the data folder, for the .desktop entries, which want a file. Rewritten at every start,
    /// so a changed drawing reaches them. Null, logged, if it cannot be written: the entries then go without an icon.
    /// </summary>
    public static string? Save()
    {
        var path = Path.Combine(PresetLibrary.DefaultRoot, "icon.png");
        try
        {
            Directory.CreateDirectory(PresetLibrary.DefaultRoot);
            using var bitmap = Render(256);
            bitmap.Save(path, new PngBitmapEncoderOptions());
            return path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn("app", $"icon not written to {path}: {e.Message}");
            return null;
        }
    }

    private static IBrush Vertical(string top, string bottom) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.Parse(top), 0), new GradientStop(Color.Parse(bottom), 1) },
    };
}
