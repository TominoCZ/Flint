namespace Flint.Core.Layout;

/// <summary>The attribute names one direction of a ConstraintLayout uses: start/end across, top/bottom down.</summary>
internal sealed record Axis(
    bool Horizontal,
    string Size,
    string[] StartToStart, string[] StartToEnd, string[] EndToEnd, string[] EndToStart,
    string[] StartMargin, string[] EndMargin,
    string Bias, string Default, string Percent, string Max)
{
    public static readonly Axis Across = new(true, "layout_width",
        ["layout_constraintStart_toStartOf", "layout_constraintLeft_toLeftOf"],
        ["layout_constraintStart_toEndOf", "layout_constraintLeft_toRightOf"],
        ["layout_constraintEnd_toEndOf", "layout_constraintRight_toRightOf"],
        ["layout_constraintEnd_toStartOf", "layout_constraintRight_toLeftOf"],
        ["layout_marginStart", "layout_marginLeft", "layout_marginHorizontal", "layout_margin"],
        ["layout_marginEnd", "layout_marginRight", "layout_marginHorizontal", "layout_margin"],
        "layout_constraintHorizontal_bias", "layout_constraintWidth_default", "layout_constraintWidth_percent",
        "layout_constraintWidth_max");

    public static readonly Axis Down = new(false, "layout_height",
        ["layout_constraintTop_toTopOf"], ["layout_constraintTop_toBottomOf"],
        ["layout_constraintBottom_toBottomOf"], ["layout_constraintBottom_toTopOf"],
        ["layout_marginTop", "layout_marginVertical", "layout_margin"],
        ["layout_marginBottom", "layout_marginVertical", "layout_margin"],
        "layout_constraintVertical_bias", "layout_constraintHeight_default", "layout_constraintHeight_percent",
        "layout_constraintHeight_max");

    /// <summary>The guideline orientation that sets a position on this axis: a vertical line sits at some x.</summary>
    public string GuidelineOrientation => Horizontal ? "vertical" : "horizontal";
}
