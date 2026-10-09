namespace Flint.App.Assets;

/// <summary>
/// One model as the app draws it. Names are drawables and layouts under <c>assets/res</c>. <see cref="Icon"/> is the
/// strip image, <see cref="ItemIcon"/> the model sheet's; amps and preamps also name their <see cref="Face"/>,
/// <see cref="Cab"/> and <see cref="KnobStyle"/>, while a pedal's face is in its editor layout.
/// </summary>
/// <param name="Editor">The panel's layout, or a phone and a tablet one (<c>_phone</c>, <c>_tablet</c>).</param>
internal sealed record CatalogModel(
    string Id,
    string DspId,
    string? Name,
    string? Icon,
    string? ItemIcon,
    string? Face,
    string? KnobStyle,
    string? Cab,
    IReadOnlyList<string> Editor);
