using Microsoft.Win32;
using Flint.Core.Logging;

namespace Flint.App;

/// <summary>
/// The tray's "Start at login": an entry in <c>~/.config/autostart/</c> on Linux, a value under the
/// user's <c>Run</c> key on Windows. Either starts this same build (<see cref="DesktopEntry.Command"/>) with
/// <see cref="Minimized"/>, so at login it waits in the tray; any other start shows the window.
/// </summary>
internal static class Autostart
{
    /// <summary>The argument the login start passes: start in the tray, window hidden.</summary>
    public const string Minimized = "--minimized";

    /// <summary>What the login start runs.</summary>
    public static IReadOnlyList<string> Command => [.. DesktopEntry.Command, Minimized];

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private static string LinuxPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "autostart", DesktopEntry.FileName);

    public static bool IsOn
    {
        get
        {
            if (OperatingSystem.IsWindows()) return Registry.CurrentUser.OpenSubKey(RunKey)?.GetValue(DesktopEntry.WmClass) is not null;
            return File.Exists(LinuxPath);
        }
    }

    /// <summary>Turns it on or off. Throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> if it cannot.</summary>
    public static void Set(bool on, string? icon)
    {
        Write(on, icon);
        Log.Info("app", on ? "starts at login" : "no longer starts at login");
    }

    /// <summary>At start: if it is on, points it at this build (and at the current arguments), so moving the executable does not break it.</summary>
    public static void Refresh(string? icon)
    {
        try
        {
            if (IsOn) Write(true, icon);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn("app", $"autostart entry not refreshed: {e.Message}");
        }
    }

    private static void Write(bool on, string? icon)
    {
        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (on) key.SetValue(DesktopEntry.WmClass, string.Join(' ', Command.Select(a => $"\"{a}\"")));
            else key.DeleteValue(DesktopEntry.WmClass, throwOnMissingValue: false);
        }
        else if (on) DesktopEntry.Write(LinuxPath, DesktopEntry.Text(Command, icon));
        else File.Delete(LinuxPath);
    }
}
