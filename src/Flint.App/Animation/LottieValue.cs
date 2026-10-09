using System.Text.Json;

namespace Flint.App.Animation;

/// <summary>
/// One Lottie property: a constant, or keyframes eased by cubic Béziers (a keyframe's <c>o</c> and <c>i</c> tangents shape
/// the segment it starts), as Lottie's own players interpolate them. Values are arrays (a scalar has one).
/// </summary>
internal sealed class LottieValue
{
    private readonly double[] _constant = [];
    private readonly List<(double T, double[] Value, (double X, double Y)? Out, (double X, double Y)? In, bool Hold)> _keys = [];

    public LottieValue(JsonElement? property, params double[] fallback)
    {
        if (property is not { } p || !p.TryGetProperty("k", out var k))
        {
            _constant = fallback;
            return;
        }
        if (k.ValueKind == JsonValueKind.Array && k.GetArrayLength() > 0 && k[0].ValueKind == JsonValueKind.Object)
        {
            foreach (var key in k.EnumerateArray())
                _keys.Add((key.GetProperty("t").GetDouble(), key.TryGetProperty("s", out var s) ? Numbers(s) : fallback,
                    Tangent(key, "o"), Tangent(key, "i"), key.TryGetProperty("h", out var h) && h.GetInt32() == 1));
        }
        else
        {
            _constant = Numbers(k);
        }
    }

    public double this[double frame] => At(frame)[0];

    public double[] At(double frame)
    {
        if (_keys.Count == 0) return _constant;
        if (frame <= _keys[0].T) return _keys[0].Value;
        for (var i = 0; i < _keys.Count - 1; i++)
        {
            // Both tangents of a segment live on the keyframe it starts from.
            var (t0, from, easeOut, easeIn, hold) = _keys[i];
            var (t1, to, _, _, _) = _keys[i + 1];
            if (frame >= t1) continue;
            if (hold) return from;
            var p = (frame - t0) / (t1 - t0);
            var y = easeOut is { } o && easeIn is { } e ? Bezier(o.X, o.Y, e.X, e.Y, p) : p;
            return [.. from.Select((v, n) => v + (n < to.Length ? to[n] - v : 0) * y)];
        }
        return _keys[^1].Value;
    }

    private static double[] Numbers(JsonElement e) =>
        e.ValueKind == JsonValueKind.Array ? [.. e.EnumerateArray().Select(x => x.GetDouble())] : [e.GetDouble()];

    private static (double, double)? Tangent(JsonElement key, string name) =>
        key.TryGetProperty(name, out var t) ? (First(t.GetProperty("x")), First(t.GetProperty("y"))) : null;

    private static double First(JsonElement e) => e.ValueKind == JsonValueKind.Array ? e[0].GetDouble() : e.GetDouble();

    /// <summary>CSS-style cubic-bezier(x1, y1, x2, y2) at x: find the curve's t for x by bisection, return its y.</summary>
    private static double Bezier(double x1, double y1, double x2, double y2, double x)
    {
        static double Curve(double a, double b, double t) => 3 * a * t * (1 - t) * (1 - t) + 3 * b * t * t * (1 - t) + t * t * t;
        double lo = 0, hi = 1, t = x;
        for (var i = 0; i < 30; i++)
        {
            t = (lo + hi) / 2;
            if (Curve(x1, x2, t) < x) lo = t; else hi = t;
        }
        return Curve(y1, y2, t);
    }
}
