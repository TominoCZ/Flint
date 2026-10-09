using Avalonia.Controls;
using Avalonia.VisualTree;
using Flint.App.Assets;
using Flint.App.Tone;
using Flint.Core.Layout;
using Flint.Core.Presets;

namespace Flint.App.Tests;

// Artwork deleted while the app runs: a copy of the owner's assets/ loses its sharpest images and one pedal's layout,
// then every model's panel is opened. Missing images draw as the placeholder, the pedal without a layout shows a note,
// and nothing throws. Passes with a note where there is no assets/.
[Collection(Headless.Name)]
public sealed class MissingArtworkTests(Headless headless) : IDisposable
{
    private static readonly AssetStore? Assets = AssetStore.Find();

    private readonly string _copy = Directory.CreateTempSubdirectory("flint-missing-").FullName;

    public void Dispose() => Directory.Delete(_copy, recursive: true);

    [Fact]
    public Task Every_panel_opens_with_images_and_a_layout_gone() => headless.Run(async () =>
    {
        if (Assets is null) return;
        Copy(Assets.Root, _copy);
        var store = new AssetStore(_copy);
        var values = new AndroidValues(Path.Combine(_copy, "res"));
        foreach (var dpi in new[] { "xxxhdpi", "xxhdpi" })
            if (Directory.Exists(Path.Combine(_copy, "res", $"drawable-{dpi}")))
                foreach (var image in Directory.EnumerateFiles(Path.Combine(_copy, "res", $"drawable-{dpi}"))) File.Delete(image);
        var drive = store.Catalog.Chain.Single(s => s.Label == "DRIVE").Models[0];
        File.Delete(store.Xml("layout", drive.Editor[0])!);

        var editor = new EditorHost(store, values, new LayoutContext(_ => null, values.Dimen));
        var window = new Window { Width = 400, Height = 500, Content = editor };
        window.Show();
        var placeholders = 0;
        foreach (var entry in store.Catalog.Chain.SelectMany(s => s.Order))
        {
            editor.Show(PresetJson.ReadModule(File.ReadAllText(Path.Combine(_copy, entry.Preset))));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            placeholders += editor.GetVisualDescendants().OfType<Image>().Count(i => ReferenceEquals(i.Source, AssetStore.Missing.Value));
            if (entry.DspId == drive.DspId)
                Assert.Contains(editor.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("its artwork is missing") == true);
        }
        Assert.True(placeholders > 0, "no image was drawn as the placeholder");

        Directory.Delete(Path.Combine(_copy, "res"), recursive: true); // the whole folder gone: the crash the owner hit
        Assert.Null(store.Xml("layout", drive.Editor[0]));
        Assert.Same(AssetStore.Missing.Value, store.Bitmap("never_loaded_before"));
        await Task.CompletedTask;
    });

    private static void Copy(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        foreach (var dir in Directory.EnumerateDirectories(from)) Copy(dir, Path.Combine(to, Path.GetFileName(dir)));
    }
}
