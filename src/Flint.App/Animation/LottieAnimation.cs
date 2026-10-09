using System.Text.Json;
using Avalonia;
using Avalonia.Media;

namespace Flint.App.Animation;

/// <summary>
/// The part of Lottie the app's connecting rings use (<c>res/raw/ripple_white_spin.json</c>): precomposition and shape
/// layers with anchor, position, scale, rotation and opacity; groups of ellipses with strokes and fills; and trim paths.
/// Anything else in a file is skipped, so an unknown feature draws less rather than failing.
/// </summary>
internal sealed class LottieAnimation
{
    private readonly JsonElement _root;
    private readonly Dictionary<string, JsonElement> _assets;

    private LottieAnimation(JsonElement root)
    {
        _root = root;
        Width = root.GetProperty("w").GetDouble();
        Height = root.GetProperty("h").GetDouble();
        FrameRate = root.GetProperty("fr").GetDouble();
        InFrame = root.GetProperty("ip").GetDouble();
        OutFrame = root.GetProperty("op").GetDouble();
        _assets = root.TryGetProperty("assets", out var assets)
            ? assets.EnumerateArray().Where(a => a.TryGetProperty("layers", out _)).ToDictionary(a => a.GetProperty("id").GetString()!, a => a)
            : [];
    }

    public double Width { get; }
    public double Height { get; }
    public double FrameRate { get; }
    public double InFrame { get; }
    public double OutFrame { get; }

    public static LottieAnimation Load(string path) => new(JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone());

    /// <summary>Draws one frame in the animation's own coordinates (Width × Height).</summary>
    public void Render(DrawingContext context, double frame) => Layers(context, _root.GetProperty("layers"), frame);

    private void Layers(DrawingContext context, JsonElement layers, double frame)
    {
        // Lottie lists the top layer first.
        foreach (var layer in layers.EnumerateArray().Reverse())
        {
            if (frame < Number(layer, "ip", double.MinValue) || frame >= Number(layer, "op", double.MaxValue)) continue;
            var local = frame - Number(layer, "st", 0);
            var ks = layer.TryGetProperty("ks", out var t) ? t : default;
            using (context.PushTransform(Transform(ks, local)))
            using (context.PushOpacity(Opacity(ks, local)))
            {
                switch (Number(layer, "ty", -1))
                {
                    case 0 when layer.TryGetProperty("refId", out var id) && _assets.TryGetValue(id.GetString()!, out var asset):
                        Layers(context, asset.GetProperty("layers"), local);
                        break;
                    case 4 when layer.TryGetProperty("shapes", out var shapes):
                        Shapes(context, shapes, local, Trim(shapes, local));
                        break;
                }
            }
        }
    }

    private static void Shapes(DrawingContext context, JsonElement shapes, double frame, (double Start, double End)? trim)
    {
        var items = shapes.EnumerateArray().ToList();
        var paths = items.Where(i => Type(i) == "el").Select(e => Ellipse(e, frame, trim)).Where(g => g is not null).ToList();
        foreach (var item in items)
        {
            switch (Type(item))
            {
                case "gr":
                    var it = item.GetProperty("it");
                    var tr = it.EnumerateArray().FirstOrDefault(i => Type(i) == "tr");
                    using (context.PushTransform(Transform(tr, frame)))
                    using (context.PushOpacity(Opacity(tr, frame)))
                        Shapes(context, it, frame, Trim(it, frame) ?? trim);
                    break;
                case "st":
                    var pen = new Pen(Brush(item, frame), new LottieValue(Prop(item, "w"), 1)[frame])
                    {
                        LineCap = Number(item, "lc", 2) switch { 1 => PenLineCap.Flat, 3 => PenLineCap.Square, _ => PenLineCap.Round },
                    };
                    foreach (var path in paths) context.DrawGeometry(null, pen, path!);
                    break;
                case "fl":
                    foreach (var path in paths) context.DrawGeometry(Brush(item, frame), null, path!);
                    break;
            }
        }
    }

    /// <summary>An ellipse path from the top, clockwise, cut to the trim's start and end (fractions of its length).</summary>
    private static Geometry? Ellipse(JsonElement e, double frame, (double Start, double End)? trim)
    {
        var size = new LottieValue(Prop(e, "s"), 0, 0).At(frame);
        var centre = new LottieValue(Prop(e, "p"), 0, 0).At(frame);
        double rx = size[0] / 2, ry = size[1] / 2;
        var (start, end) = trim ?? (0, 1);
        if (end - start <= 0.0005) return null;
        if (end - start >= 0.9995) return new EllipseGeometry(new Rect(centre[0] - rx, centre[1] - ry, 2 * rx, 2 * ry));
        Point At(double f) => new(centre[0] + rx * Math.Sin(2 * Math.PI * f), centre[1] - ry * Math.Cos(2 * Math.PI * f));
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(At(start), false);
            g.ArcTo(At(end), new Size(rx, ry), 0, end - start > 0.5, SweepDirection.Clockwise);
            g.EndFigure(false);
        }
        return geometry;
    }

    /// <summary>A trim path among the items: start and end as fractions, its offset (degrees) added to both.</summary>
    private static (double, double)? Trim(JsonElement items, double frame)
    {
        if (items.EnumerateArray().FirstOrDefault(i => Type(i) == "tm") is not { ValueKind: JsonValueKind.Object } tm) return null;
        double s = new LottieValue(Prop(tm, "s"), 0)[frame] / 100, e = new LottieValue(Prop(tm, "e"), 100)[frame] / 100;
        var offset = new LottieValue(Prop(tm, "o"), 0)[frame] / 360;
        if (s > e) (s, e) = (e, s);
        return (s + offset, e + offset);
    }

    private static Matrix Transform(JsonElement ks, double frame)
    {
        if (ks.ValueKind != JsonValueKind.Object) return Matrix.Identity;
        var a = new LottieValue(Prop(ks, "a"), 0, 0).At(frame);
        var p = new LottieValue(Prop(ks, "p"), 0, 0).At(frame);
        var s = new LottieValue(Prop(ks, "s"), 100, 100).At(frame);
        var r = new LottieValue(Prop(ks, "r"), 0)[frame];
        return Matrix.CreateTranslation(-a[0], -a[1]) * Matrix.CreateScale(s[0] / 100, s[1] / 100)
               * Matrix.CreateRotation(r * Math.PI / 180) * Matrix.CreateTranslation(p[0], p[1]);
    }

    private static double Opacity(JsonElement ks, double frame) =>
        ks.ValueKind == JsonValueKind.Object ? new LottieValue(Prop(ks, "o"), 100)[frame] / 100 : 1;

    private static IBrush Brush(JsonElement item, double frame)
    {
        var c = new LottieValue(Prop(item, "c"), 1, 1, 1, 1).At(frame);
        var alpha = new LottieValue(Prop(item, "o"), 100)[frame] / 100;
        return new SolidColorBrush(Color.FromArgb((byte)(255 * alpha), (byte)(255 * c[0]), (byte)(255 * c[1]), (byte)(255 * c[2])));
    }

    private static JsonElement? Prop(JsonElement e, string name) => e.TryGetProperty(name, out var p) ? p : null;

    private static string? Type(JsonElement e) => e.TryGetProperty("ty", out var t) ? t.GetString() : null;

    private static double Number(JsonElement e, string name, double fallback) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : fallback;
}
