namespace Flint.App.Assets;

/// <summary>A row of the model sheet: the model, and the knob values the app loads with it. A reverb row is a type of bias.reverb.</summary>
internal sealed record PickerEntry(string Preset, string DspId, float? ReverbType);
