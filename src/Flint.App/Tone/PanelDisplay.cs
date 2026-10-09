using System.Globalization;

namespace Flint.App.Tone;

/// <summary>The big number over the top while a control is held, in the app's formats.</summary>
internal static class PanelDisplay
{
    /// <summary>A knob's "Normal" format: the value × 10, one decimal.</summary>
    public static string Knob(double value) => (value * 10).ToString("F1", CultureInfo.InvariantCulture);

    /// <summary>A slider: its display range (−10 to 10 by default, as <c>Slider</c>), one decimal.</summary>
    public static string Slider(double value) => (-10 + value * 20).ToString("F1", CultureInfo.InvariantCulture);
}
