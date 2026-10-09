namespace Flint.App.Assets;

/// <summary>One knob of an amp panel: its style (<c>KnobNew07</c>) and the parameter index it turns.</summary>
internal sealed record AmpPanelKnob(string KnobType, int Param);
