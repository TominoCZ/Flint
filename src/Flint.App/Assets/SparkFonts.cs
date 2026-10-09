using Avalonia.Media;
using Avalonia.Media.Fonts;
using Flint.Core.Logging;

namespace Flint.App.Assets;

/// <summary>
/// The app's own fonts (Raleway PG in seven weights, Roboto) from <c>assets/res/font/</c>, registered with Avalonia so
/// that <see cref="Raleway"/> and <see cref="Roboto"/> work anywhere a font family does.
/// </summary>
internal sealed class SparkFonts : FontCollectionBase
{
    private static readonly Uri CollectionKey = new("fonts:spark");

    // Avalonia's form for a family in a custom collection: "<collection key>#<family name>".
    // A missing family must fall back, not throw while drawing: hence ", $Default".
    public static readonly FontFamily Raleway = new("fonts:spark#Raleway PG, $Default"), Roboto = new("fonts:spark#Roboto, $Default");

    private SparkFonts(string fontFolder)
    {
        foreach (var file in Directory.EnumerateFiles(fontFolder, "*.?tf"))
        {
            using var stream = File.OpenRead(file);
            if (!TryAddGlyphTypeface(stream, out _)) Log.Warn("app", $"font {Path.GetFileName(file)} not loaded");
        }
    }

    public override Uri Key => CollectionKey;

    /// <summary>Call once at startup, before any window uses the families.</summary>
    public static void Register(AssetStore assets) =>
        FontManager.Current.AddFontCollection(new SparkFonts(Path.Combine(assets.Root, "res", "font")));
}
