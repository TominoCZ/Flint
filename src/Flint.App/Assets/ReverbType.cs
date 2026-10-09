namespace Flint.App.Assets;

/// <summary>A reverb as the model sheet shows it: one value of bias.reverb's param 6, with its own name and pedal images.</summary>
internal sealed record ReverbType(float Value, string Name, string Face, string Icon, string ItemIcon);
