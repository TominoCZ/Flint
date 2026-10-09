using System.Globalization;

namespace Flint.Core.Layout;

/// <summary>
/// Solves the children of one container. Sizes and positions are worked out on demand and remembered, so a node can
/// depend on any sibling in any order; a cycle is a <see cref="FormatException"/> naming the node.
/// </summary>
internal sealed class ConstraintPass
{
    private readonly LayoutNode _container;
    private readonly double _width, _height;
    private readonly LayoutContext _context;
    private readonly Dictionary<string, LayoutNode> _byId;
    private readonly Dictionary<LayoutNode, (double W, double H)> _sizes = [];
    private readonly Dictionary<(LayoutNode, bool), double> _positions = [];
    private readonly HashSet<(LayoutNode, char)> _busy = [];

    public ConstraintPass(LayoutNode container, double width, double height, LayoutContext context)
    {
        (_container, _width, _height, _context) = (container, width, height, context);
        _byId = container.Children.Where(c => c.Id is not null).GroupBy(c => c.Id!).ToDictionary(g => g.Key, g => g.First());
    }

    public LayoutBox Box(LayoutNode node)
    {
        var (w, h) = Size(node);
        return new LayoutBox(Position(node, Axis.Across), Position(node, Axis.Down), w, h);
    }

    // ---- sizes ----

    private (double W, double H) Size(LayoutNode node)
    {
        if (_sizes.TryGetValue(node, out var known)) return known;
        using var _ = Enter(node, 's');
        var size = node.IsGone || node.Tag == "Guideline" ? (0, 0) : Measure(node);
        return _sizes[node] = size;
    }

    private (double, double) Measure(LayoutNode node)
    {
        double? w = Determined(node, Axis.Across), h = Determined(node, Axis.Down);
        if (Ratio(node) is var (prefix, r))
        {
            // A percent size always stands and the other side follows the ratio. Otherwise the prefix names the side
            // that is computed from the other (Android's docs), and when the other cannot be worked out it goes the
            // other way: the phone's amp face "W,1038:555" sits in a chain, so its width fills and its height follows;
            // a knob "H,1:1" has one horizontal anchor, so its height fills and its width follows. With no prefix,
            // the side that is not fixed follows the one that is.
            bool widthFollows;
            if (IsPercent(node, Axis.Across) != IsPercent(node, Axis.Down)) widthFollows = IsPercent(node, Axis.Down);
            else if (prefix is 'W' or 'H') widthFollows = prefix == 'W' ? h is not null : w is null;
            else widthFollows = IsFlexible(node, Axis.Across) && (!IsFlexible(node, Axis.Down) || w is null || (h is not null && w / r > h));
            if (widthFollows && h is not null) w = h * r;
            else if (!widthFollows && w is not null) h = w / r;
        }
        return (Clamp(node, Axis.Across, w ?? 0), Clamp(node, Axis.Down, h ?? 0));
    }

    /// <summary>The size the axis's own spec gives, or null when only a ratio can tell.</summary>
    private double? Determined(LayoutNode node, Axis axis)
    {
        var spec = node[axis.Size] ?? "wrap_content";
        var parent = axis.Horizontal ? _width : _height;
        switch (spec)
        {
            case "match_parent" or "fill_parent":
                return parent - Margin(node, axis.StartMargin) - Margin(node, axis.EndMargin);
            case "wrap_content":
                return _context.Measure(node) is { } natural ? (axis.Horizontal ? natural.Width : natural.Height) : null;
        }
        var dp = _context.Dimen(spec);
        if (dp != 0) return dp;
        // 0dp: "match constraint": a percent of the parent, or the span between both anchors.
        if (IsPercent(node, axis)) return parent * Number(node[axis.Percent], 1);
        if (Ratio(node) is not null && Chained(node, axis)) return null;
        return Anchor(node, axis, start: true) is { } a && Anchor(node, axis, start: false) is { } b
            ? Math.Max(0, b - a - Margin(node, axis.StartMargin) - Margin(node, axis.EndMargin))
            : null;
    }

    /// <summary>A percent size: ConstraintLayout 2 takes <c>…_percent</c> alone as one, with or without <c>…_default="percent"</c>.</summary>
    private static bool IsPercent(LayoutNode node, Axis axis) =>
        (node[axis.Size] ?? "wrap_content") == "0dp" && (node[axis.Default] == "percent" || node[axis.Percent] is not null);

    private bool IsFlexible(LayoutNode node, Axis axis) => (node[axis.Size] ?? "wrap_content") is "0dp" or "wrap_content";

    private double Clamp(LayoutNode node, Axis axis, double size) =>
        node[axis.Max] is { } max ? Math.Min(size, _context.Dimen(max)) : size;

    private static (char? Prefix, double R)? Ratio(LayoutNode node)
    {
        if (node["layout_constraintDimensionRatio"] is not { Length: > 0 } text) return null;
        char? prefix = null;
        if (text.IndexOf(',') is var comma and > 0)
        {
            prefix = char.ToUpperInvariant(text[..comma].Trim()[0]);
            text = text[(comma + 1)..];
        }
        var parts = text.Split(':');
        var r = parts.Length == 2 ? Number(parts[0], 1) / Number(parts[1], 1) : Number(parts[0], 1);
        return (prefix, r);
    }

    // ---- positions ----

    private double Position(LayoutNode node, Axis axis)
    {
        if (_positions.TryGetValue((node, axis.Horizontal), out var known)) return known;
        using var _ = Enter(node, axis.Horizontal ? 'x' : 'y');
        double position;
        if (node.Tag == "Guideline")
        {
            var along = axis.Horizontal ? _width : _height;
            position = node["orientation"] != axis.GuidelineOrientation ? 0
                : node["layout_constraintGuide_percent"] is { } percent ? Number(percent, 0) * along
                : node["layout_constraintGuide_begin"] is { } begin ? _context.Dimen(begin)
                : node["layout_constraintGuide_end"] is { } end ? along - _context.Dimen(end) : 0;
        }
        else if (ChainHead(node, axis) is { } head)
        {
            position = ChainPositions(head, axis)[node];
        }
        else
        {
            var size = axis.Horizontal ? Size(node).W : Size(node).H;
            var bias = Number(node[axis.Bias], 0.5);
            // A bias of 0 or 1 holds the view against one anchor, so the other is not worked out: the Auto Wah's pedal
            // is anchored to the far end of a chain that starts at the pedal's own end.
            var a = bias >= 1 && Anchored(node, axis, start: false) ? null : Anchor(node, axis, start: true) + Margin(node, axis.StartMargin);
            var b = bias <= 0 && a is not null ? null : Anchor(node, axis, start: false) - Margin(node, axis.EndMargin);
            position = (a, b) switch
            {
                ({ } s, { } e) => s + bias * (e - s - size),
                ({ } s, null) => s,
                (null, { } e) => e - size,
                _ => 0,
            };
        }
        return _positions[(node, axis.Horizontal)] = position;
    }

    /// <summary>The coordinate a side is anchored to: the parent's edge or a sibling's side, or null when unanchored.</summary>
    private double? Anchor(LayoutNode node, Axis axis, bool start)
    {
        var (toStart, toEnd) = start ? (axis.StartToStart, axis.StartToEnd) : (axis.EndToStart, axis.EndToEnd);
        foreach (var (names, targetsEnd) in new[] { (toStart, false), (toEnd, true) })
        {
            if (names.Select(n => node[n]).FirstOrDefault(v => v is not null) is not { } target) continue;
            if (target == "parent") return targetsEnd ? (axis.Horizontal ? _width : _height) : 0;
            if (!_byId.TryGetValue(target.Split('/').Last(), out var sibling)) continue; // a view in another layout: ignored
            var at = Position(sibling, axis);
            return targetsEnd ? at + (axis.Horizontal ? Size(sibling).W : Size(sibling).H) : at;
        }
        return null;
    }

    /// <summary>Whether a side has an anchor <see cref="Anchor"/> would follow.</summary>
    private bool Anchored(LayoutNode node, Axis axis, bool start)
    {
        var (toStart, toEnd) = start ? (axis.StartToStart, axis.StartToEnd) : (axis.EndToStart, axis.EndToEnd);
        return toStart.Concat(toEnd).Any(n => node[n] is { } target && (target == "parent" || _byId.ContainsKey(target.Split('/').Last())));
    }

    // ---- packed chains (the only style the panels use) ----

    /// <summary>The node after this one in a chain: its end is anchored to the other's start and the other's start back to it.</summary>
    private LayoutNode? Next(LayoutNode node, Axis axis) =>
        Target(node, axis.EndToStart) is { } next && Target(next, axis.StartToEnd) == node ? next : null;

    private LayoutNode? Previous(LayoutNode node, Axis axis) =>
        Target(node, axis.StartToEnd) is { } previous && Target(previous, axis.EndToStart) == node ? previous : null;

    private bool Chained(LayoutNode node, Axis axis) => Next(node, axis) is not null || Previous(node, axis) is not null;

    private LayoutNode? ChainHead(LayoutNode node, Axis axis)
    {
        if (!Chained(node, axis)) return null;
        while (Previous(node, axis) is { } previous) node = previous;
        return node;
    }

    private Dictionary<LayoutNode, double> ChainPositions(LayoutNode head, Axis axis)
    {
        var members = new List<LayoutNode> { head };
        while (Next(members[^1], axis) is { } next) members.Add(next);
        double SizeOf(LayoutNode n) => axis.Horizontal ? Size(n).W : Size(n).H;
        var total = members.Sum(n => SizeOf(n) + Margin(n, axis.StartMargin) + Margin(n, axis.EndMargin));
        var start = Anchor(head, axis, start: true) ?? 0;
        var end = Anchor(members[^1], axis, start: false) ?? (axis.Horizontal ? _width : _height);
        var at = start + Number(head[axis.Bias], 0.5) * (end - start - total);
        var positions = new Dictionary<LayoutNode, double>();
        foreach (var n in members)
        {
            at += Margin(n, axis.StartMargin);
            positions[n] = _positions[(n, axis.Horizontal)] = at;
            at += SizeOf(n) + Margin(n, axis.EndMargin);
        }
        return positions;
    }

    private LayoutNode? Target(LayoutNode node, string[] names) =>
        names.Select(n => node[n]).FirstOrDefault(v => v is not null) is { } target && target != "parent"
            ? _byId.GetValueOrDefault(target.Split('/').Last())
            : null;

    // ---- helpers ----

    private double Margin(LayoutNode node, string[] names) =>
        names.Select(n => node[n]).FirstOrDefault(v => v is not null) is { } value ? _context.Dimen(value) : 0;

    private static double Number(string? text, double fallback) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : fallback;

    private Busy Enter(LayoutNode node, char what) =>
        _busy.Add((node, what)) ? new Busy(_busy, (node, what))
            : throw new FormatException($"constraint cycle at {node} in {_container}");

    private readonly struct Busy(HashSet<(LayoutNode, char)> set, (LayoutNode, char) key) : IDisposable
    {
        public void Dispose() => set.Remove(key);
    }
}
