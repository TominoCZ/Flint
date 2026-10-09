namespace Flint.App.Assets;

/// <summary>What one control of a pedal panel turns, read from the app's data binding: a parameter index, and whether it
/// is a <c>knob</c>, a <c>slider</c> or a <c>switch</c> (a 0/1 parameter).</summary>
internal sealed record PanelControl(int Param, string Kind);
