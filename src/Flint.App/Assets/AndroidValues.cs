using System.Globalization;
using System.Xml.Linq;
using Avalonia.Media;

namespace Flint.App.Assets;

/// <summary>
/// The app's <c>res/values/</c> colours, dimensions and strings, the default ones (phone, English), with
/// <c>@color/x</c>-style references followed. The only place these files are parsed.
/// </summary>
internal sealed class AndroidValues
{
    private static readonly Dictionary<string, string> AndroidColors = new()
    {
        ["white"] = "#ffffffff", ["black"] = "#ff000000", ["transparent"] = "#00000000",
    };

    private readonly Dictionary<string, string> _colors, _dimens, _strings;
    private readonly Dictionary<string, XElement> _styles;

    public AndroidValues(string resRoot)
    {
        var values = Path.Combine(resRoot, "values");
        _colors = Read(Path.Combine(values, "colors.xml"), "color");
        _dimens = Read(Path.Combine(values, "dimens.xml"), "dimen");
        _strings = Read(Path.Combine(values, "strings.xml"), "string");
        var styles = Path.Combine(values, "styles.xml");
        _styles = File.Exists(styles)
            ? XDocument.Load(styles).Root!.Elements("style").GroupBy(e => (string)e.Attribute("name")!).ToDictionary(g => g.Key, g => g.First())
            : [];
    }

    /// <summary>A colour by name (<c>grey950</c>) or as written in a layout (<c>@color/grey950</c>, <c>#99000000</c>).</summary>
    public Color Color(string nameOrValue)
    {
        var value = Follow(nameOrValue, _colors, "color");
        return value.StartsWith('#') ? Avalonia.Media.Color.Parse(value) : throw new FormatException($"not a colour: {nameOrValue}");
    }

    /// <summary>A dimension in dp (sp and px are taken as dp too) by name or as written (<c>@dimen/toolbar_height</c>, <c>20dp</c>).</summary>
    public double Dimen(string nameOrValue)
    {
        var value = Follow(nameOrValue, _dimens, "dimen");
        var number = value.TrimEnd('d', 'p', 's', 'x', 'i', 'n', 'm');
        return double.Parse(number, CultureInfo.InvariantCulture);
    }

    /// <summary>A string by name or reference, or the text itself when it is not a reference.</summary>
    public string Text(string nameOrValue) => Follow(nameOrValue, _strings, "string");

    /// <summary>
    /// A style's items by local name (<c>android:layout_width</c> becomes <c>layout_width</c>), its parent's underneath:
    /// the <c>parent</c> attribute, or else the name up to its last dot (<c>Spark.Typeface.Body</c> → <c>Spark.Typeface</c>).
    /// Null for an unknown style.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Style(string name)
    {
        if (!_styles.TryGetValue(name, out var style)) return null;
        var parent = (string?)style.Attribute("parent") is { Length: > 0 } p ? p.Split('/').Last()
            : name.LastIndexOf('.') is var dot and > 0 ? name[..dot] : null;
        var items = (parent is not null ? Style(parent) : null)?.ToDictionary(kv => kv.Key, kv => kv.Value) ?? [];
        foreach (var item in style.Elements("item"))
            items[((string)item.Attribute("name")!).Split(':').Last()] = item.Value.Trim();
        return items;
    }

    private static string Follow(string reference, Dictionary<string, string> table, string kind)
    {
        for (var hops = 0; hops < 10; hops++)
        {
            if (reference.StartsWith("@android:color/") && AndroidColors.TryGetValue(reference[15..], out var android)) return android;
            var name = reference.StartsWith('@') ? reference[(reference.IndexOf('/') + 1)..] : reference;
            if (!table.TryGetValue(name, out var next)) return reference;
            if (!next.StartsWith('@')) return next;
            reference = next;
        }
        throw new FormatException($"{kind} reference loop at {reference}");
    }

    private static Dictionary<string, string> Read(string file, string element) =>
        File.Exists(file)
            ? XDocument.Load(file).Root!.Elements(element)
                .GroupBy(e => (string)e.Attribute("name")!).ToDictionary(g => g.Key, g => g.First().Value.Trim())
            : [];
}
