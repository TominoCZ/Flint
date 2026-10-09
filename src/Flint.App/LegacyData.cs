using Microsoft.Win32;

namespace Flint.App;

/// <summary>
/// Flint was called SparkControl. On the first start after the rename, its users' data folder (presets, settings,
/// artwork, tools, logs) moves into Flint's, and its login entry follows; the old menu entry goes, since Flint writes its
/// own. The one place the old name stays.
/// </summary>
internal static class LegacyData
{
    private const string OldName = "SparkControl";

    /// <summary>
    /// Runs before anything uses the data folder, the log included, so it returns what it did for the log, or null. A
    /// part Flint already has (an install may have put artwork there first) is kept, and the old one left where it was.
    /// </summary>
    public static string? Move(string localData, string config, string root)
    {
        var old = Path.Combine(localData, OldName);
        if (!Directory.Exists(old)) return null;
        try
        {
            Directory.CreateDirectory(root);
            var kept = new List<string>();
            foreach (var entry in Directory.EnumerateFileSystemEntries(old))
            {
                var target = Path.Combine(root, Path.GetFileName(entry));
                if (Path.Exists(target)) kept.Add(Path.GetFileName(entry));
                else if (Directory.Exists(entry)) Directory.Move(entry, target);
                else File.Move(entry, target);
            }
            if (kept.Count == 0) Directory.Delete(old);

            File.Delete(Path.Combine(localData, "applications", OldName + ".desktop"));
            var autostart = Path.Combine(config, "autostart", OldName + ".desktop");
            if (File.Exists(autostart)) File.Move(autostart, Path.Combine(config, "autostart", DesktopEntry.FileName), overwrite: true); // Autostart.Refresh then points it here
            if (OperatingSystem.IsWindows()) MoveRunValue();
            return kept.Count == 0 ? $"moved {old} to {root}" : $"moved {old} to {root}, but for {string.Join(", ", kept)}, which Flint already had";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"could not move {old} to {root}: {e.Message}";
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void MoveRunValue()
    {
        using var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
        if (run?.GetValue(OldName) is not { } command) return;
        run.SetValue(DesktopEntry.WmClass, command); // Autostart.Refresh then points it here
        run.DeleteValue(OldName);
    }
}
