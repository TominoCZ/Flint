using System.Text.Json;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Flint.Core.Library;
using Flint.Core.Logging;

namespace Flint.App.Assets;

/// <summary>
/// The art and panel definitions read from the user's own Spark app by the setup screen (or scripts/extract-assets.sh) into a gitignored
/// <c>assets/</c>. Looked for in <c>$FLINT_ASSETS</c>, then beside the executable and each folder
/// above it (so a build in the repo finds the repo's), then in the data folder.
/// </summary>
internal sealed class AssetStore
{
    private const string Manifest = "manifest.json";

    // Sharpest first. drawable/ is the one-resolution art (editor faces, pedals, cabs); it counts as mdpi, as on Android.
    private static readonly (string Folder, double Scale)[] Densities =
        [("drawable-xxxhdpi", 4), ("drawable-xxhdpi", 3), ("drawable", 1), ("drawable-xhdpi", 2), ("drawable-hdpi", 1.5), ("drawable-mdpi", 1)];
    private static readonly string[] ImageTypes = [".png", ".9.png", ".webp", ".jpg"]; // .9.png: a compiled nine-patch, borders already stripped
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly Dictionary<string, Bitmap> _bitmaps = [];

    internal AssetStore(string root)
    {
        Root = root;
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, Manifest)));
        Version = manifest.RootElement.GetProperty("version").GetString() ?? "?";
        Catalog = JsonSerializer.Deserialize<Catalog>(File.ReadAllText(Path.Combine(root, "catalog.json")), Json)
                  ?? throw new FormatException("catalog.json is empty");
    }

    public string Root { get; }

    /// <summary>The Spark app version the assets came from.</summary>
    public string Version { get; }

    public Catalog Catalog { get; }

    /// <summary>Where the app looks, in order; the window names the first when nothing is found.</summary>
    public static IEnumerable<string> Candidates()
    {
        if (Environment.GetEnvironmentVariable("FLINT_ASSETS") is { Length: > 0 } custom) yield return custom;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            yield return Path.Combine(dir.FullName, "assets");
        yield return Path.Combine(PresetLibrary.DefaultRoot, "assets");
    }

    /// <summary>The first complete assets folder, or null (logged) when there is none or it cannot be read.</summary>
    public static AssetStore? Find()
    {
        var root = Candidates().FirstOrDefault(c => File.Exists(Path.Combine(c, Manifest)));
        if (root is null)
        {
            Log.Warn("app", "no assets: the setup screen takes them from the Spark app");
            return null;
        }
        try
        {
            var store = new AssetStore(root);
            Log.Info("app", $"assets from Spark {store.Version} in {root}");
            return store;
        }
        catch (Exception e) when (e is IOException or JsonException or FormatException or KeyNotFoundException)
        {
            Log.Error("app", $"assets in {root} unreadable", e);
            return null;
        }
    }

    /// <summary>The sharpest image file for an Android drawable name, or null. XML drawables are not images: see <see cref="Xml"/>.</summary>
    public Drawable? Image(string name)
    {
        foreach (var (folder, scale) in Densities)
            foreach (var type in ImageTypes)
                if (Path.Combine(Root, "res", folder, name + type) is var path && File.Exists(path))
                    return new Drawable(path, scale);
        return null;
    }

    /// <summary>An XML drawable (layer-list, selector, vector) or layout under <c>res/</c>, or null, also when <c>res/</c> itself is gone.</summary>
    public string? Xml(string kind, string name) =>
        Directory.Exists(Path.Combine(Root, "res"))
            ? Directory.EnumerateDirectories(Path.Combine(Root, "res"), kind + "*").Select(d => Path.Combine(d, name + ".xml")).FirstOrDefault(File.Exists)
            : null;

    /// <summary>A file in <c>res/raw/</c> (the Lottie animations), or null.</summary>
    public string? Raw(string fileName) => Path.Combine(Root, "res", "raw", fileName) is var path && File.Exists(path) ? path : null;

    /// <summary>
    /// The image, decoded once and kept. One whose file is missing or unreadable (deleted while the app runs, say) is
    /// <see cref="Missing"/> instead, logged once, so a screen draws with a gap rather than the app crashing.
    /// </summary>
    public Bitmap Bitmap(string name)
    {
        lock (_bitmaps)
        {
            if (_bitmaps.TryGetValue(name, out var cached)) return cached;
            try
            {
                return _bitmaps[name] = new Bitmap(Image(name)?.Path ?? throw new FileNotFoundException("no such file"));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                Log.Warn("app", $"image {name} missing or unreadable in {Root} ({e.Message}): drawn as a gap; update the Spark artwork from the tray");
                return _bitmaps[name] = Missing.Value;
            }
        }
    }

    /// <summary>What a missing image is drawn as: one translucent grey pixel, stretched to wherever the image would be.</summary>
    internal static readonly Lazy<Bitmap> Missing = new(() =>
    {
        var bitmap = new WriteableBitmap(new PixelSize(1, 1), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var pixels = bitmap.Lock())
            System.Runtime.InteropServices.Marshal.WriteInt32(pixels.Address, 0x40303030); // premultiplied: 25 % of #c0c0c0
        return bitmap;
    });
}
