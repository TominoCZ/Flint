using Flint.App.Assets;
using Flint.Core.Library;

namespace Flint.App.Setup;

/// <summary>Where setup keeps things, all under Flint's own data folder.</summary>
internal static class SetupPaths
{
    /// <summary>adb and jadx, when the system has no adb of its own. Kept for the next update.</summary>
    public static string Tools => Path.Combine(PresetLibrary.DefaultRoot, "tools");

    /// <summary>The app's APKs and jadx's output while setup runs. Deleted when it is done.</summary>
    public static string Work => Path.Combine(PresetLibrary.DefaultRoot, "setup");

    public static string Apks => Path.Combine(Work, "apks");

    public static string Decompiled => Path.Combine(Work, "decompiled");

    /// <summary>The assets folder in use, so an update replaces it; on a first run, the data folder's.</summary>
    public static string Assets(AssetStore? current) => current?.Root ?? Path.Combine(PresetLibrary.DefaultRoot, "assets");
}
