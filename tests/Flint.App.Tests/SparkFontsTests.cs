using Avalonia.Media;
using Flint.App.Assets;

namespace Flint.App.Tests;

// The app's own fonts come from assets/res/font; without them Avalonia silently falls back to its default face.
[Collection(Headless.Name)]
public sealed class SparkFontsTests(Headless headless)
{
    [Fact]
    public async Task Raleway_PG_resolves_to_the_apps_own_font_in_each_weight_used()
    {
        if (AssetStore.Find() is not { } assets) return;
        await headless.Session.Dispatch(() =>
        {
            SparkFonts.Register(assets);
            foreach (var weight in new[] { FontWeight.SemiBold, FontWeight.Bold })
            {
                Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface(SparkFonts.Raleway, FontStyle.Normal, weight), out var face));
                Assert.StartsWith("Raleway PG", face!.FamilyName); // the SemiBold file names itself "Raleway PG SemiBold"
                Assert.Equal(weight, face.Weight);
            }
        }, CancellationToken.None);
    }
}
