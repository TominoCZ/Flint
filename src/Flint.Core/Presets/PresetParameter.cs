namespace Flint.Core.Presets;

/// <summary>One knob of a module. Almost always one value from 0.0 to 1.0; the format allows several.</summary>
public sealed record PresetParameter(int Index, IReadOnlyList<float> Values);
