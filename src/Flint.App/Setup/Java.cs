using System.Text.RegularExpressions;

namespace Flint.App.Setup;

/// <summary>
/// The Java that jadx runs on. Setup does not install one (the owner's call): with none, or one too old, it names the
/// command for this OS: winget's on Windows, and on Linux the distribution's, read from /etc/os-release.
/// </summary>
internal static partial class Java
{
    /// <summary>jadx needs Java 11 or newer.</summary>
    public const int Needed = 11;

    /// <summary>The java jadx's launcher will use: <c>$JAVA_HOME/bin/java</c>, else the one on PATH, else null.</summary>
    public static string? Find() =>
        Environment.GetEnvironmentVariable("JAVA_HOME") is { Length: > 0 } home && File.Exists(Path.Combine(home, "bin", CommandLine.Exe("java")))
            ? Path.Combine(home, "bin", CommandLine.Exe("java"))
            : CommandLine.OnPath("java");

    /// <summary>The major version <c>java -version</c> prints (<c>"21.0.2"</c> is 21, <c>"1.8.0_392"</c> is 8), or null.</summary>
    public static int? Major(string versionOutput)
    {
        var m = VersionLine().Match(versionOutput);
        if (!m.Success) return null;
        var parts = m.Groups[1].Value.Split('.');
        return int.Parse(parts[0] == "1" && parts.Length > 1 ? parts[1] : parts[0]);
    }

    /// <summary>The command that installs Java on this computer, or null where it is not known here.</summary>
    public static string? InstallCommand() =>
        OperatingSystem.IsWindows() ? "winget install EclipseAdoptium.Temurin.21.JRE" : InstallCommand(OsRelease());

    /// <summary>The command that installs Java on this Linux distribution, or null for one not known here.</summary>
    public static string? InstallCommand(string osRelease)
    {
        var ids = OsIds(osRelease);
        if (ids.Overlaps(["debian", "ubuntu"])) return "sudo apt install openjdk-17-jre-headless"; // 17: the newest Debian 12 has
        if (ids.Overlaps(["fedora", "rhel", "centos"])) return "sudo dnf install java-21-openjdk-headless";
        if (ids.Overlaps(["arch"])) return "sudo pacman -S jre21-openjdk-headless";
        if (ids.Overlaps(["suse", "opensuse"])) return "sudo zypper install java-21-openjdk-headless";
        return null;
    }

    /// <summary>The distribution and the ones it is like (<c>ID</c>, <c>ID_LIKE</c>), lower case.</summary>
    public static HashSet<string> OsIds(string osRelease) =>
        [.. OsId().Matches(osRelease).SelectMany(m => m.Groups[1].Value.Trim('"').Split(' ', StringSplitOptions.RemoveEmptyEntries)).Select(i => i.ToLowerInvariant())];

    /// <summary>/etc/os-release, or "" where there is none.</summary>
    public static string OsRelease() => File.Exists("/etc/os-release") ? File.ReadAllText("/etc/os-release") : "";

    [GeneratedRegex(@"version ""([0-9._]+)")]
    private static partial Regex VersionLine();

    [GeneratedRegex(@"^ID(?:_LIKE)?=(.*)$", RegexOptions.Multiline)]
    private static partial Regex OsId();
}
