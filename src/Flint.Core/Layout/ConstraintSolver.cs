namespace Flint.Core.Layout;

/// <summary>
/// Places every node of an Android layout, for the ConstraintLayout subset the app's panels use:
/// guidelines, side anchors to the parent or a sibling, margins, dimension ratios, biases, percent sizes, fixed, wrap
/// and match sizes, packed chains, nested layouts, and gone views. Boxes are in dp, relative to the root.
/// </summary>
public static class ConstraintSolver
{
    public static Dictionary<LayoutNode, LayoutBox> Solve(LayoutNode root, double width, double height, LayoutContext context)
    {
        var boxes = new Dictionary<LayoutNode, LayoutBox> { [root] = new(0, 0, width, height) };
        Place(root, boxes[root], context, boxes);
        return boxes;
    }

    private static void Place(LayoutNode container, LayoutBox box, LayoutContext context, Dictionary<LayoutNode, LayoutBox> boxes)
    {
        var pass = new ConstraintPass(container, box.Width, box.Height, context);
        foreach (var child in container.Children)
        {
            var childBox = pass.Box(child).Offset(box.X, box.Y);
            boxes[child] = childBox;
            if (child.Children.Count > 0) Place(child, childBox, context, boxes);
        }
    }
}
