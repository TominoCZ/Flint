using Flint.Core.Logging;

namespace Flint.App;

/// <summary>
/// The freedesktop <c>.desktop</c> entries that start the app on Linux: the launcher's, which the
/// published executable keeps up to date at every start (so a moved binary still launches), and the autostart one
/// (<see cref="Autostart"/>). The icon is the Spark app's own in <c>assets/</c>, pointed at, never copied (decision 17).
/// </summary>
internal static class DesktopEntry
{
    /// <summary>The X11 WM_CLASS the window carries (Program), so the desktop puts the window under this entry's icon.</summary>
    public const string WmClass = "Flint";

    public const string FileName = WmClass + ".desktop";

    /// <summary>The command that starts this same build: the executable itself, or <c>dotnet Flint.dll</c> under run.sh.</summary>
    public static IReadOnlyList<string> Command { get; } = CommandOf(Environment.ProcessPath ?? "Flint", AppContext.BaseDirectory);

    /// <summary>Whether this build runs under the <c>dotnet</c> host (run.sh) rather than as the published executable.</summary>
    public static bool Hosted => Command.Count > 1;

    private static string LauncherPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "applications", FileName);

    /// <summary>
    /// At start: the published executable writes its launcher entry; a development build leaves the launcher alone. A
    /// failure costs only the menu entry, so it is logged.
    /// </summary>
    public static void Install(string? icon)
    {
        if (!OperatingSystem.IsLinux() || Hosted) return;
        try
        {
            if (Write(LauncherPath, Text(Command, icon))) Log.Info("app", $"wrote the launcher entry {LauncherPath}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn("app", $"launcher entry not written: {e.Message}");
        }
    }

    public static string Text(IReadOnlyList<string> command, string? icon)
    {
        List<string> lines =
        [
            "[Desktop Entry]", "Type=Application", "Name=Flint", "Comment=Control a Positive Grid Spark 2 amp",
            $"Exec={string.Join(' ', command.Select(Quote))}", "Terminal=false", "Categories=AudioVideo;Audio;", $"StartupWMClass={WmClass}",
        ];
        if (icon is not null) lines.Add($"Icon={icon}");
        return string.Join('\n', lines) + "\n";
    }

    /// <summary>Writes the file if its text differs, and says whether it did.</summary>
    public static bool Write(string path, string text)
    {
        if (File.Exists(path) && File.ReadAllText(path) == text) return false;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return true;
    }

    /// <summary>
    /// One Exec argument as the Desktop Entry spec wants it: <c>%</c> doubled, and quoted when it has reserved characters,
    /// with <c>"`$\</c> escaped inside the quotes and then every backslash escaped again, as a string value's are.
    /// </summary>
    public static string Quote(string argument)
    {
        var escaped = argument.Replace("%", "%%");
        if (escaped.Length > 0 && !escaped.Any(c => char.IsWhiteSpace(c) || "\"'\\><~|&;$*?#()`".Contains(c))) return escaped;
        foreach (var c in "\\\"`$") escaped = escaped.Replace(c.ToString(), "\\" + c);
        return $"\"{escaped}\"".Replace("\\", "\\\\");
    }

    /// <summary>Under the dotnet host the process alone is not the app: the dll beside this build goes with it.</summary>
    internal static IReadOnlyList<string> CommandOf(string process, string appFolder) =>
        Path.GetFileNameWithoutExtension(process) == "dotnet" ? [process, Path.Combine(appFolder, "Flint.dll")] : [process];
}
