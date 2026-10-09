namespace Flint.Core.Layout;

/// <summary>Where a node ends up, in dp from the top-left corner of the layout that was solved.</summary>
public readonly record struct LayoutBox(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;

    public LayoutBox Offset(double x, double y) => this with { X = X + x, Y = Y + y };
}
