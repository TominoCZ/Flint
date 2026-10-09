namespace Flint.App.Assets;

/// <summary>One place in the chain ("DRIVE"): the models that can sit there, and the order the model sheet lists them in.</summary>
internal sealed record ChainSlot(string Label, IReadOnlyList<CatalogModel> Models, IReadOnlyList<PickerEntry> Order);
