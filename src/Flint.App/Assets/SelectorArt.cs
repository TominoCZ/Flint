using System.Xml.Linq;
using Avalonia.Media.Imaging;

namespace Flint.App.Assets;

/// <summary>
/// A two-state <c>&lt;selector&gt;</c> drawable (<c>led_03.xml</c>, <c>switch_03.xml</c>): the image for
/// <c>state_checked="true"</c> and the one for every other state. Either is null when it is not a bitmap.
/// </summary>
internal sealed record SelectorArt(Bitmap? Checked, Bitmap? Unchecked)
{
    private static readonly XNamespace Android = "http://schemas.android.com/apk/res/android";

    public static SelectorArt? Load(AssetStore assets, string name)
    {
        if (assets.Xml("drawable", name) is not { } file) return null;
        var root = XDocument.Load(file).Root!;
        if (root.Name.LocalName != "selector") return null;
        var items = root.Elements("item").ToList();
        Bitmap? Image(XElement? item)
        {
            var reference = (string?)item?.Attribute(Android + "drawable") ?? (string?)item?.Element("bitmap")?.Attribute(Android + "src");
            return reference?.Split('/').Last() is { } drawable && assets.Image(drawable) is not null ? assets.Bitmap(drawable) : null;
        }
        return new SelectorArt(Image(items.FirstOrDefault(i => (string?)i.Attribute(Android + "state_checked") == "true")),
            Image(items.FirstOrDefault(i => i.Attribute(Android + "state_checked") is null)));
    }
}
