using System.Xml.Linq;

namespace Flint.Core.Layout;

/// <summary>
/// One element of an Android layout file: its tag's last segment (<c>ImageView</c>, <c>Knob</c>,
/// <c>Guideline</c>), its id without <c>@+id/</c>, its attributes by local name (<c>layout_width</c>, <c>srcCompat</c>),
/// and its children. The only place layout XML is parsed. A <c>style="@style/X"</c> contributes its items as defaults
/// under the element's own attributes, as Android applies them (the EQ's sliders get their size that way).
/// </summary>
public sealed class LayoutNode
{
    private LayoutNode(XElement element, Func<string, IReadOnlyDictionary<string, string>?>? styles)
    {
        Tag = element.Name.LocalName[(element.Name.LocalName.LastIndexOf('.') + 1)..];
        var own = element.Attributes().Where(a => !a.IsNamespaceDeclaration)
            .GroupBy(a => a.Name.LocalName).ToDictionary(g => g.Key, g => g.First().Value.Trim());
        var attributes = own.TryGetValue("style", out var style) && styles?.Invoke(style.Split('/').Last()) is { } items
            ? items.ToDictionary(kv => kv.Key, kv => kv.Value)
            : [];
        foreach (var (name, value) in own) attributes[name] = value;
        Attributes = attributes;
        Id = this["id"]?.Split('/').Last();
        Children = [.. element.Elements().Select(e => new LayoutNode(e, styles))];
    }

    public string Tag { get; }
    public string? Id { get; }
    public IReadOnlyDictionary<string, string> Attributes { get; }
    public IReadOnlyList<LayoutNode> Children { get; }

    public string? this[string name] => Attributes.GetValueOrDefault(name);

    public bool IsGone => this["visibility"] == "gone";

    /// <summary>This node and everything under it, depth first.</summary>
    public IEnumerable<LayoutNode> All() => Children.SelectMany(c => c.All()).Prepend(this);

    /// <param name="styles">A style's items by local name (<c>layout_width</c>), its parents' included; null for none.</param>
    public static LayoutNode Parse(string xml, Func<string, IReadOnlyDictionary<string, string>?>? styles = null) =>
        new(XDocument.Parse(xml).Root ?? throw new FormatException("empty layout"), styles);

    public static LayoutNode Load(string path, Func<string, IReadOnlyDictionary<string, string>?>? styles = null) =>
        Parse(File.ReadAllText(path), styles);

    public override string ToString() => Id is null ? Tag : $"{Tag}#{Id}";
}
