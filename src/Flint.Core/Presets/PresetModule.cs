namespace Flint.Core.Presets;

/// <summary>One block of the signal chain: its model (<c>dspId</c>, e.g. "GK800"), whether it is on, and its knobs.</summary>
public sealed record PresetModule(string DspId, bool Active, IReadOnlyList<PresetParameter> Parameters);
