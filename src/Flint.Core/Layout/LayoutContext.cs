namespace Flint.Core.Layout;

/// <summary>What the solver needs from outside: a node's natural size for <c>wrap_content</c> (an image's or a text's), and
/// dimension values such as <c>7dp</c> or <c>@dimen/toolbar_height</c> in dp.</summary>
public sealed record LayoutContext(Func<LayoutNode, (double Width, double Height)?> Measure, Func<string, double> Dimen);
