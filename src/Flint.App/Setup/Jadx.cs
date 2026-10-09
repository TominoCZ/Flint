using System.Text.RegularExpressions;

namespace Flint.App.Setup;

/// <summary>
/// jadx, which turns the Spark app back into resources and Java sources for <see cref="AssetExtractor"/>. Downloaded
/// from its GitHub releases into the tools folder on first use.
/// </summary>
internal static partial class Jadx
{
    private static string Launcher => Path.Combine(SetupPaths.Tools, "jadx", "bin", OperatingSystem.IsWindows() ? "jadx.bat" : "jadx");

    public static bool Installed => File.Exists(Launcher);

    public static async Task InstallAsync(IProgress<double> progress, CancellationToken cancel)
    {
        var url = await Download.LatestReleaseAsync("skylot/jadx", ReleaseZip(), cancel);
        await Download.ZipAsync(url, Path.Combine(SetupPaths.Tools, "jadx"), progress, cancel);
        if (!Installed) throw new IOException("the jadx download has no bin/jadx");
    }

    /// <summary>
    /// Decompiles <paramref name="apk"/> into <paramref name="output"/>, reporting classes done of all. jadx exits 3 when
    /// some of the app's 17,000 classes do not decompile (about 150 do not), which the catalog never needs, so success is
    /// what it wrote, not its exit code.
    /// </summary>
    public static async Task DecompileAsync(string apk, string output, IProgress<(int Done, int Total)> progress, CancellationToken cancel)
    {
        if (Directory.Exists(output)) Directory.Delete(output, recursive: true);
        // Uncapped, it took 30 GB on a big machine; 3 GB and 4 threads take about 45 s.
        // ponytail: decompiles every class though the catalog reads a few hundred; jadx's --single-class per class would be the upgrade.
        var (code, text) = await CommandLine.RunAsync(Launcher,
            ["-d", output, "--show-bad-code", "--deobf-min", "2", "-j", Math.Min(Environment.ProcessorCount, 4).ToString(), apk], cancel,
            line =>
            {
                if (Progress(line) is { } p) progress.Report(p);
            },
            new Dictionary<string, string> { ["JAVA_OPTS"] = "-Xmx3g" });
        if (!File.Exists(Path.Combine(output, "resources", "AndroidManifest.xml")) || !Directory.Exists(Path.Combine(output, "sources")))
            throw new IOException($"jadx failed (exit {code}): {CommandLine.Tail(text)}");
    }

    /// <summary>jadx's <c>INFO  - progress: 3144 of 17803 (17%)</c>, or null for any other line.</summary>
    public static (int Done, int Total)? Progress(string line) =>
        ProgressLine().Match(line) is { Success: true } m ? (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value)) : null;

    [GeneratedRegex(@"^jadx-[0-9.]+\.zip$")]
    private static partial Regex ReleaseZip();

    [GeneratedRegex(@"progress: (\d+) of (\d+)")]
    private static partial Regex ProgressLine();
}
