using System.Text.Json;
using Flint.App.Animation;

namespace Flint.App.Tests;

// Lottie keyframes as the app's rings use them: eased by cubic Béziers between keyframes, held at the ends.
public sealed class LottieValueTests
{
    [Fact]
    public void Keyframes_ease_along_their_bezier_and_hold_outside_their_range()
    {
        var value = Parse("""{"a":1,"k":[{"t":0,"s":[0],"o":{"x":[0.42],"y":[0]},"i":{"x":[0.58],"y":[1]}},{"t":100,"s":[360]}]}""");

        Assert.Equal(0, value[-5]);
        Assert.Equal(180, value[50], 3);            // ease-in-out is symmetric: half way at half time
        Assert.Equal(0.1291 * 360, value[25], 0); // CSS ease-in-out at 25%: a slow start (linear would be 90)
        Assert.Equal(360, value[200]);
    }

    [Fact]
    public void Linear_tangents_are_linear_and_a_constant_is_constant()
    {
        Assert.Equal(25, Parse("""{"a":1,"k":[{"t":0,"s":[0],"o":{"x":[0],"y":[0]},"i":{"x":[1],"y":[1]}},{"t":4,"s":[100]}]}""")[1], 3);
        Assert.Equal([56.0, 56.0], Parse("""{"a":0,"k":[56,56]}""").At(30));
    }

    private static LottieValue Parse(string json) => new(JsonDocument.Parse(json).RootElement);
}
