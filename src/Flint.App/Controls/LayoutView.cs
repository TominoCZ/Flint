using Avalonia;
using Avalonia.Controls;
using Flint.Core.Layout;

namespace Flint.App.Controls;

/// <summary>
/// Shows an Android layout (an <c>editor_*.xml</c> panel): one control per node that draws or reacts, created by the
/// caller, placed where <see cref="ConstraintSolver"/> puts it, later nodes on top as on Android. One dp is one unit;
/// the screen scales the whole view.
/// </summary>
internal sealed class LayoutView : Panel
{
    private readonly LayoutNode _root;
    private readonly LayoutContext _context;
    private readonly List<(LayoutNode Node, Control Control)> _placed = [];

    public LayoutView(LayoutNode root, LayoutContext context, Func<LayoutNode, Control?> create)
    {
        (_root, _context) = (root, context);
        foreach (var node in root.All().Skip(1).Where(n => !n.IsGone && n["visibility"] != "invisible"))
            if (create(node) is { } control)
            {
                _placed.Add((node, control));
                Children.Add(control);
            }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children) child.Measure(availableSize);
        return new Size(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var boxes = ConstraintSolver.Solve(_root, finalSize.Width, finalSize.Height, _context);
        foreach (var (node, control) in _placed)
            control.Arrange(boxes.TryGetValue(node, out var b) ? new Rect(b.X, b.Y, b.Width, b.Height) : default);
        return finalSize;
    }
}
