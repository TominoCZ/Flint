namespace Flint.App.Assets;

/// <summary>
/// An image file and the density Android drew it for: <see cref="Scale"/> pixels per dp (1 for <c>drawable/</c> and
/// mdpi, 1.5 hdpi, 2 xhdpi, 3 xxhdpi, 4 xxxhdpi), so a layout can size it as the app does.
/// </summary>
internal sealed record Drawable(string Path, double Scale);
