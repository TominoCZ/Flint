using System.Globalization;
using System.Xml.Linq;
using Avalonia.Media.Imaging;

namespace Flint.App.Assets;

/// <summary>
/// A knob style's images, from its layer-list (<c>res/drawable/knob_07.xml</c>): a background and an indicator that
/// rotates from <see cref="FromDegrees"/> at 0 to <see cref="ToDegrees"/> at 1 (0 and 270 in every style).
/// </summary>
internal sealed record KnobArt(Bitmap Background, Bitmap Indicator, double FromDegrees, double ToDegrees)
{
    private static readonly XNamespace Android = "http://schemas.android.com/apk/res/android";

    /// <summary>The art for a catalog style (<c>JamUpKnob_07</c>), a layout's (<c>jamUp_07</c>) or a panel file's (<c>KnobNew07</c>).</summary>
    public static KnobArt Load(AssetStore assets, string style)
    {
        var number = new string([.. style.Where(char.IsDigit)]).PadLeft(2, '0');
        var drawable = $"knob_{number}";
        var file = assets.Xml("drawable", drawable) ?? throw new FileNotFoundException($"no {drawable}.xml for knob style {style}");
        var items = XDocument.Load(file).Root!.Elements("item").ToList();
        string Source(XElement item) =>
            Ref(item.Attribute(Android + "drawable")) ?? Ref(item.Descendants().Select(d => d.Attribute(Android + "src") ?? d.Attribute(Android + "drawable")).FirstOrDefault(a => a is not null))
            ?? throw new FormatException($"{drawable}: a layer without an image");
        var rotate = items.Select(i => i.Element("rotate")).FirstOrDefault(r => r is not null)
                     ?? throw new FormatException($"{drawable}: no rotating layer");
        double Degrees(string name, double fallback) =>
            double.TryParse((string?)rotate.Attribute(Android + name), CultureInfo.InvariantCulture, out var d) ? d : fallback;
        return new KnobArt(assets.Bitmap(Source(items[0])), assets.Bitmap(Ref(rotate.Attribute(Android + "drawable"))!),
            Degrees("fromDegrees", 0), Degrees("toDegrees", 360));
    }

    private static string? Ref(XAttribute? attribute) => attribute?.Value.Split('/').Last();
}
