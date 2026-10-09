using System.Globalization;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Flint.App.Assets;

/// <summary>
/// An Android <c>&lt;vector&gt;</c> drawable as an Avalonia image, sized in dp. <c>pathData</c> is SVG path syntax, which
/// <see cref="StreamGeometry.Parse"/> takes as is. Paths, fills, strokes, alpha, group transforms and clip-paths are
/// drawn, and linear and radial gradients, inline (<c>aapt:attr</c>) or in their own drawable as jadx writes them out;
/// a sweep gradient is drawn flat in its first colour.
/// </summary>
internal static class VectorArt
{
    private static readonly XNamespace Android = "http://schemas.android.com/apk/res/android";

    /// <summary>A drawable as the app shows it: its bitmap if it has one, else its vector; null if it has neither.</summary>
    public static IImage? Drawable(AssetStore assets, AndroidValues values, string name) =>
        assets.Image(name) is not null ? assets.Bitmap(name) : Load(assets, values, name);

    /// <summary>
    /// A drawable as an image at its own size in dp (a bitmap's pixels over its density, a vector's declared size), as
    /// Android draws an icon an ImageButton centres; or stretched to <paramref name="size"/> dp square.
    /// </summary>
    public static Image Picture(AssetStore assets, AndroidValues values, string name, double size = double.NaN)
    {
        var image = new Image
        {
            Source = Drawable(assets, values, name), Width = size, Height = size, Stretch = Stretch.Uniform,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        if (!double.IsNaN(size)) return image;
        if (assets.Image(name) is { } file)
        {
            var pixels = assets.Bitmap(name).PixelSize;
            (image.Width, image.Height) = (pixels.Width / file.Scale, pixels.Height / file.Scale);
        }
        else if (image.Source is { } vector)
        {
            (image.Width, image.Height) = (vector.Size.Width, vector.Size.Height);
        }
        return image;
    }

    public static DrawingImage? Load(AssetStore assets, AndroidValues values, string name)
    {
        if (assets.Xml("drawable", name) is not { } file) return null;
        var root = XDocument.Load(file).Root!;
        if (root.Name.LocalName != "vector") return null;
        double Attr(XElement e, string a, double fallback) =>
            double.TryParse(((string?)e.Attribute(Android + a))?.Replace("dp", ""), CultureInfo.InvariantCulture, out var v) ? v : fallback;
        double width = Attr(root, "width", 24), height = Attr(root, "height", 24);
        double viewportWidth = Attr(root, "viewportWidth", width), viewportHeight = Attr(root, "viewportHeight", height);

        var drawing = new DrawingGroup { Transform = new ScaleTransform(width / viewportWidth, height / viewportHeight) };
        // A transparent box over the whole viewport, so the image keeps its declared size whatever its paths cover.
        drawing.Children.Add(new GeometryDrawing { Geometry = new RectangleGeometry(new Rect(0, 0, viewportWidth, viewportHeight)), Brush = Brushes.Transparent });
        Add(root, drawing);
        return new DrawingImage(drawing);

        void Add(XElement parent, DrawingGroup into)
        {
            foreach (var element in parent.Elements())
            {
                if (element.Name.LocalName == "clip-path" && (string?)element.Attribute(Android + "pathData") is { } clip)
                {
                    // Clips what follows it in its group: drawn into a clipped group of its own, so the image keeps its size.
                    var clipped = new DrawingGroup { ClipGeometry = StreamGeometry.Parse(clip) };
                    into.Children.Add(clipped);
                    into = clipped;
                }
                else if (element.Name.LocalName == "group")
                {
                    var group = new DrawingGroup { Transform = GroupTransform(element) };
                    Add(element, group);
                    into.Children.Add(group);
                }
                else if (element.Name.LocalName == "path" && (string?)element.Attribute(Android + "pathData") is { } data)
                {
                    into.Children.Add(new GeometryDrawing
                    {
                        Geometry = StreamGeometry.Parse(data),
                        Brush = Paint(element, "fillColor", Attr(element, "fillAlpha", 1)),
                        Pen = Paint(element, "strokeColor", Attr(element, "strokeAlpha", 1)) is { } stroke ? new Pen(stroke, Attr(element, "strokeWidth", 1)) : null,
                    });
                }
            }
        }

        // A colour, a gradient drawable it names, or an inline gradient under the path (aapt:attr name="android:fillColor").
        IBrush? Paint(XElement path, string attribute, double alpha)
        {
            var reference = (string?)path.Attribute(Android + attribute);
            if (reference?.StartsWith("@drawable/") == true)
                return assets.Xml("drawable", reference[10..]) is { } gradient ? Gradient(XDocument.Load(gradient).Root!, alpha) : null;
            if (reference is not null) return new SolidColorBrush(values.Color(reference), alpha);
            var inline = path.Elements().FirstOrDefault(e => e.Name.LocalName == "attr" && ((string?)e.Attribute("name"))?.EndsWith(attribute) == true)
                ?.Descendants().FirstOrDefault(d => d.Name.LocalName == "gradient");
            return inline is null ? null : Gradient(inline, alpha);
        }

        // In the vector's viewport units, which is what Avalonia's absolute brush units are inside the drawing.
        IBrush Gradient(XElement gradient, double alpha)
        {
            var items = gradient.Elements().Where(e => e.Name.LocalName == "item").ToList();
            var stops = items.Count > 0
                ? items.Select(i => new GradientStop(values.Color((string)i.Attribute(Android + "color")!), Attr(i, "offset", 0)))
                : new[] { ("startColor", 0.0), ("centerColor", 0.5), ("endColor", 1.0) }
                    .Where(c => gradient.Attribute(Android + c.Item1) is not null)
                    .Select(c => new GradientStop(values.Color((string)gradient.Attribute(Android + c.Item1)!), c.Item2));
            var collection = new GradientStops();
            collection.AddRange(stops);
            Point At(string x, string y) => new(Attr(gradient, x, 0), Attr(gradient, y, 0));
            return (string?)gradient.Attribute(Android + "type") switch
            {
                "radial" => new RadialGradientBrush
                {
                    Center = new RelativePoint(At("centerX", "centerY"), RelativeUnit.Absolute),
                    GradientOrigin = new RelativePoint(At("centerX", "centerY"), RelativeUnit.Absolute),
                    RadiusX = new RelativeScalar(Attr(gradient, "gradientRadius", 1), RelativeUnit.Absolute),
                    RadiusY = new RelativeScalar(Attr(gradient, "gradientRadius", 1), RelativeUnit.Absolute),
                    GradientStops = collection, Opacity = alpha,
                },
                "sweep" => new SolidColorBrush(collection.Count > 0 ? collection[0].Color : Colors.Transparent, alpha),
                _ => new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(At("startX", "startY"), RelativeUnit.Absolute),
                    EndPoint = new RelativePoint(At("endX", "endY"), RelativeUnit.Absolute),
                    GradientStops = collection, Opacity = alpha,
                },
            };
        }

        Transform GroupTransform(XElement g)
        {
            double px = Attr(g, "pivotX", 0), py = Attr(g, "pivotY", 0);
            var matrix = Matrix.CreateTranslation(-px, -py) * Matrix.CreateScale(Attr(g, "scaleX", 1), Attr(g, "scaleY", 1))
                         * Matrix.CreateRotation(Attr(g, "rotation", 0) * Math.PI / 180) * Matrix.CreateTranslation(px, py)
                         * Matrix.CreateTranslation(Attr(g, "translateX", 0), Attr(g, "translateY", 0));
            return new MatrixTransform(matrix);
        }
    }
}
