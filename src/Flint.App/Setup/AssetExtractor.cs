using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Flint.App.Setup;

/// <summary>
/// Copies what Flint draws from the Spark app into an assets folder, then builds its catalog.json: the C# twin of
/// scripts/extract-assets.sh, which ExtractionParityTests keeps in step with it. Reads jadx's output
/// (<c>decompiled/</c>) and the app's APKs (<c>apks/</c>: base and splits).
/// </summary>
internal static class AssetExtractor
{
    private static readonly string[] Densities = ["xxxhdpi", "xxhdpi", "xhdpi", "hdpi", "mdpi"];

    /// <summary>
    /// Builds <paramref name="target"/> beside it and swaps it in only once complete, so a failure leaves the old one.
    /// Returns the catalog's summary. Throws <see cref="InvalidDataException"/> when the app is not what this expects.
    /// </summary>
    public static string Extract(string decompiled, string apks, string target, IProgress<string>? progress = null)
    {
        var decoded = Path.Combine(decompiled, "resources");
        var manifest = Path.Combine(decoded, "AndroidManifest.xml");
        if (!File.Exists(manifest)) throw new InvalidDataException($"no decoded app in {decompiled}");
        var staging = target + ".new";
        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        var res = Directory.CreateDirectory(Path.Combine(staging, "res")).FullName;

        progress?.Report("Copying layouts, images and fonts");
        // Layouts, XML drawables and the one-resolution art (editor faces, pedals, cabs, knobs), colours, dimensions, strings, fonts.
        foreach (var prefix in new[] { "layout", "drawable", "values", "color", "font" })
            foreach (var dir in Directory.EnumerateDirectories(Path.Combine(decoded, "res"), prefix + "*"))
                CopyDirectory(dir, Path.Combine(res, Path.GetFileName(dir)));
        // The app's Lottie animations (the connecting screen's rings); res/raw also holds sounds, which are not needed.
        Directory.CreateDirectory(Path.Combine(res, "raw"));
        foreach (var json in Directory.EnumerateFiles(Path.Combine(decoded, "res", "raw"), "*.json"))
            File.Copy(json, Path.Combine(res, "raw", Path.GetFileName(json)));

        progress?.Report("Copying the sharpest images");
        DensityImages(apks, res);
        // The amp panels (background, knob type and parameter per amp), and per chain slot the model sheet's order with
        // each model's starting knob values.
        foreach (var folder in new[] { "Configuration", "ModulePresets" })
            CopyDirectory(Path.Combine(decoded, "assets", folder), Path.Combine(staging, folder));

        progress?.Report("Building the list of amps and effects");
        var summary = CatalogBuilder.Build(decompiled, staging);
        WriteManifest(manifest, Path.Combine(staging, "manifest.json"));
        Swap(staging, target);
        return summary;
    }

    /// <summary>
    /// The strip and picker icons are density PNGs, in a density split. An .apkm has one per density, and xxhdpi's is
    /// the sharpest (there is almost no xxxhdpi art). A phone has only its own, holding the closest art there is for its
    /// screen, so some images may be one density and some another. Its XML is binary AXML, so only images come from it.
    /// </summary>
    private static void DensityImages(string apks, string res)
    {
        var split = Path.Combine(apks, "split_config.xxhdpi.apk");
        if (!File.Exists(split))
            split = Directory.EnumerateFiles(apks, "split_config.*dpi.apk").Order(StringComparer.Ordinal).FirstOrDefault()
                    ?? throw new InvalidDataException("the app has no screen-density part (split_config.*dpi.apk)");
        using var zip = ZipFile.OpenRead(split);
        foreach (var dpi in Densities)
        {
            var folder = $"res/drawable-{dpi}-v4/";
            foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith(folder) && (e.Name.EndsWith(".png") || e.Name.EndsWith(".webp"))))
            {
                var dir = Directory.CreateDirectory(Path.Combine(res, $"drawable-{dpi}")).FullName;
                entry.ExtractToFile(Path.Combine(dir, entry.Name), overwrite: true);
            }
        }
    }

    private static void WriteManifest(string androidManifest, string target)
    {
        var xml = File.ReadAllText(androidManifest);
        string Attribute(string name) => Regex.Match(xml, $" {name}=\"([^\"]+)\"").Groups[1].Value;
        var manifest = new JsonObject
        {
            ["app"] = Attribute("package"),
            ["version"] = Attribute("android:versionName"),
            ["extracted"] = DateTime.Now.ToString("yyyy-MM-dd"),
        };
        File.WriteAllText(target, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Puts <paramref name="staging"/> where <paramref name="target"/> was, and only then deletes the old one.</summary>
    private static void Swap(string staging, string target)
    {
        var old = target + ".old";
        if (Directory.Exists(old)) Directory.Delete(old, recursive: true);
        if (Directory.Exists(target)) Directory.Move(target, old);
        Directory.Move(staging, target);
        if (Directory.Exists(old)) Directory.Delete(old, recursive: true);
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.EnumerateDirectories(from)) CopyDirectory(dir, Path.Combine(to, Path.GetFileName(dir)));
    }
}
