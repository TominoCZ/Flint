using System.Globalization;
using Flint.Core.Layout;

namespace Flint.Core.Tests;

// The ConstraintLayout subset the app's panels use, on small layouts written here, with the
// numbers worked out by hand. The real panels are checked against the phone's captures in the headless render.
public sealed class ConstraintSolverTests
{
    private static readonly LayoutContext Context = new(
        n => n["tools_natural"] is { } s ? (double.Parse(s.Split('x')[0], CultureInfo.InvariantCulture), double.Parse(s.Split('x')[1], CultureInfo.InvariantCulture)) : null,
        v => double.Parse(v.Replace("dp", ""), CultureInfo.InvariantCulture));

    [Fact]
    public void A_match_constraint_view_fills_between_guidelines()
    {
        var boxes = Solve(400, 200, """
            <Guideline android:id="@+id/a" android:orientation="vertical" app:layout_constraintGuide_percent="0.25"/>
            <Guideline android:id="@+id/b" android:orientation="vertical" app:layout_constraintGuide_percent="0.75"/>
            <View android:id="@+id/v" android:layout_width="0dp" android:layout_height="40dp"
                app:layout_constraintStart_toStartOf="@+id/a" app:layout_constraintEnd_toEndOf="@+id/b"
                app:layout_constraintTop_toTopOf="parent" app:layout_constraintBottom_toBottomOf="parent"/>
            """);

        Assert.Equal(new LayoutBox(100, 80, 200, 40), boxes["v"]);
    }

    [Fact]
    public void A_ratio_whose_base_side_is_unknown_works_the_other_way()
    {
        // A knob "H,1:1" with only a start anchor across: its width cannot be worked out, so its height fills between
        // the guidelines and the width follows, as the phone draws the amp's knobs.
        var boxes = Solve(300, 300, """
            <Guideline android:id="@+id/top" android:orientation="horizontal" app:layout_constraintGuide_percent="0.5"/>
            <Guideline android:id="@+id/bottom" android:orientation="horizontal" app:layout_constraintGuide_percent="0.6"/>
            <Guideline android:id="@+id/left" android:orientation="vertical" app:layout_constraintGuide_percent="0.2"/>
            <View android:id="@+id/knob" android:layout_width="0dp" android:layout_height="0dp"
                app:layout_constraintDimensionRatio="H, 1:1" app:layout_constraintStart_toStartOf="@+id/left"
                app:layout_constraintTop_toTopOf="@+id/top" app:layout_constraintBottom_toBottomOf="@+id/bottom"/>
            """);

        Assert.Equal(new LayoutBox(60, 150, 30, 30), boxes["knob"]);
    }

    [Fact]
    public void A_bias_of_0_holds_a_view_at_its_start_without_its_end_anchor()
    {
        // The Auto Wah: the pedal's end is anchored to the meter's end, and the meter's chain starts at the pedal's end.
        // Bias 0 puts the pedal at the parent's start, so that loop is never followed.
        var boxes = Solve(400, 200, """
            <ImageView android:id="@+id/pedal" android:layout_width="100dp" android:layout_height="50dp"
                app:layout_constraintStart_toStartOf="parent" app:layout_constraintEnd_toEndOf="@+id/meter"
                app:layout_constraintHorizontal_bias="0" app:layout_constraintTop_toTopOf="parent"/>
            <View android:id="@+id/meter" android:layout_width="10dp" android:layout_height="50dp"
                app:layout_constraintStart_toEndOf="@+id/pedal" app:layout_constraintEnd_toStartOf="@+id/power"
                app:layout_constraintTop_toTopOf="parent"/>
            <View android:id="@+id/power" android:layout_width="90dp" android:layout_height="50dp"
                app:layout_constraintStart_toEndOf="@+id/meter" app:layout_constraintEnd_toEndOf="parent"
                app:layout_constraintTop_toTopOf="parent"/>
            """);

        Assert.Equal(0, boxes["pedal"].X);
        Assert.Equal(100 + (300 - 100) / 2.0, boxes["meter"].X); // the chain packed in the middle of what is left
    }

    [Fact]
    public void A_packed_chain_stacks_its_members_at_the_bias()
    {
        // The amp panel's head and cab: the head's bottom to the cab's top and back, packed against the top.
        var boxes = Solve(200, 400, """
            <ImageView android:id="@+id/head" android:layout_width="0dp" android:layout_height="0dp"
                app:layout_constraintDimensionRatio="W,2:1" app:layout_constraintVertical_chainStyle="packed"
                app:layout_constraintVertical_bias="0"
                app:layout_constraintStart_toStartOf="parent" app:layout_constraintEnd_toEndOf="parent"
                app:layout_constraintTop_toTopOf="parent" app:layout_constraintBottom_toTopOf="@+id/cab"/>
            <ImageView android:id="@+id/cab" android:layout_width="match_parent" android:layout_height="wrap_content"
                app:layout_constraintDimensionRatio="4:1"
                app:layout_constraintStart_toStartOf="parent" app:layout_constraintEnd_toEndOf="parent"
                app:layout_constraintTop_toBottomOf="@+id/head"/>
            """);

        Assert.Equal(new LayoutBox(0, 0, 200, 100), boxes["head"]);
        Assert.Equal(new LayoutBox(0, 100, 200, 50), boxes["cab"]);
    }

    [Fact]
    public void A_percent_width_sits_at_its_bias_inside_a_nested_layout()
    {
        // The amp's power switch: 8% of the panel's width, biased 4.2% across and 85% down.
        var boxes = Solve(500, 300, """
            <androidx.constraintlayout.widget.ConstraintLayout android:id="@+id/inner" android:layout_width="400dp"
                android:layout_height="200dp" app:layout_constraintStart_toStartOf="parent" app:layout_constraintTop_toTopOf="parent"
                android:layout_marginStart="50dp" android:layout_marginTop="20dp">
                <ToggleButton android:id="@+id/power" android:layout_width="0dp" android:layout_height="0dp"
                    app:layout_constraintWidth_default="percent" app:layout_constraintWidth_percent="0.08"
                    app:layout_constraintDimensionRatio="W, 1:1"
                    app:layout_constraintHorizontal_bias="0.042" app:layout_constraintVertical_bias="0.85"
                    app:layout_constraintStart_toStartOf="parent" app:layout_constraintEnd_toEndOf="parent"
                    app:layout_constraintTop_toTopOf="parent" app:layout_constraintBottom_toBottomOf="parent"/>
            </androidx.constraintlayout.widget.ConstraintLayout>
            """);

        var power = boxes["power"];
        Assert.Equal(32, power.Width, 6);
        Assert.Equal(32, power.Height, 6);
        Assert.Equal(50 + 0.042 * (400 - 32), power.X, 6);
        Assert.Equal(20 + 0.85 * (200 - 32), power.Y, 6);
    }

    [Fact]
    public void A_percent_alone_is_a_percent_size_and_the_ratio_follows_it()
    {
        // The echo tape's tempo switch: 6.63% wide, "H, 22:25", centred against all four parent edges. Without the
        // percent rule it filled the whole panel.
        var boxes = Solve(300, 300, """
            <ToggleButton android:id="@+id/s" android:layout_width="0dp" android:layout_height="0dp"
                app:layout_constraintWidth_percent="0.1" app:layout_constraintDimensionRatio="H, 1:2"
                app:layout_constraintStart_toStartOf="parent" app:layout_constraintEnd_toEndOf="parent"
                app:layout_constraintTop_toTopOf="parent" app:layout_constraintBottom_toBottomOf="parent"/>
            """);

        Assert.Equal(new LayoutBox(135, 120, 30, 60), boxes["s"]);
    }

    [Fact]
    public void With_both_sides_known_the_prefix_names_the_side_computed_from_the_other()
    {
        // Android's documented reading of "H,2:1": the height follows the width.
        var boxes = Solve(200, 300, """
            <View android:id="@+id/v" android:layout_width="0dp" android:layout_height="0dp"
                app:layout_constraintDimensionRatio="H,2:1"
                app:layout_constraintStart_toStartOf="parent" app:layout_constraintEnd_toEndOf="parent"
                app:layout_constraintTop_toTopOf="parent" app:layout_constraintBottom_toBottomOf="parent"/>
            """);

        Assert.Equal(new LayoutBox(0, 100, 200, 100), boxes["v"]);
    }

    [Fact]
    public void A_style_supplies_sizes_the_element_leaves_out()
    {
        // The EQ's sliders: their 0dp width and height come from style="@style/EqSlider".
        var root = LayoutNode.Parse("""
            <ConstraintLayout xmlns:android="http://schemas.android.com/apk/res/android" xmlns:app="http://schemas.android.com/apk/res-auto">
                <Slider android:id="@+id/s" style="@style/EqSlider" android:layout_height="40dp"
                    app:layout_constraintStart_toStartOf="parent" app:layout_constraintEnd_toEndOf="parent"/>
            </ConstraintLayout>
            """, name => name == "EqSlider" ? new Dictionary<string, string> { ["layout_width"] = "0dp", ["layout_height"] = "0dp" } : null);
        var slider = root.Children[0];

        Assert.Equal(("0dp", "40dp"), (slider["layout_width"], slider["layout_height"])); // its own attribute wins
        Assert.Equal(100, ConstraintSolver.Solve(root, 100, 100, Context)[slider].Width);
    }

    [Fact]
    public void Wrap_content_takes_the_natural_size_and_one_anchor_pins_one_side()
    {
        var boxes = Solve(300, 100, """
            <ImageView android:id="@+id/b" android:layout_width="wrap_content" android:layout_height="wrap_content"
                tools_natural="40x20" android:layout_marginEnd="5dp"
                app:layout_constraintEnd_toEndOf="parent" app:layout_constraintBottom_toBottomOf="parent"/>
            <TextView android:id="@+id/gone" android:visibility="gone" android:layout_width="wrap_content"
                android:layout_height="wrap_content" tools_natural="40x20"/>
            """);

        Assert.Equal(new LayoutBox(255, 80, 40, 20), boxes["b"]);
        Assert.Equal(0, boxes["gone"].Width);
    }

    [Fact]
    public void A_constraint_cycle_is_reported_with_the_node()
    {
        var error = Assert.Throws<FormatException>(() => Solve(100, 100, """
            <View android:id="@+id/a" android:layout_width="10dp" android:layout_height="10dp" app:layout_constraintStart_toEndOf="@+id/b"/>
            <View android:id="@+id/b" android:layout_width="10dp" android:layout_height="10dp" app:layout_constraintStart_toEndOf="@+id/a"/>
            """));
        Assert.Contains("cycle", error.Message);
    }

    private static Dictionary<string, LayoutBox> Solve(double width, double height, string children)
    {
        var root = LayoutNode.Parse($"""
            <androidx.constraintlayout.widget.ConstraintLayout xmlns:android="http://schemas.android.com/apk/res/android"
                xmlns:app="http://schemas.android.com/apk/res-auto">{children}</androidx.constraintlayout.widget.ConstraintLayout>
            """);
        return ConstraintSolver.Solve(root, width, height, Context)
            .Where(kv => kv.Key.Id is not null).ToDictionary(kv => kv.Key.Id!, kv => kv.Value);
    }
}
