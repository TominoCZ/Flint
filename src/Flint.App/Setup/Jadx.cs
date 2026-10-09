using System.Text.RegularExpressions;
using Flint.Core.Logging;

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

    // Where the catalog starts reading (ModelEnums, EditorLayouts, PanelBindings, the knob styles). Everything else it
    // reads is at most two references away: a model's editor factory or fragment, then the fragment's view binding; the
    // binding mapper's binding class, then its base class.
    private static readonly string[] CatalogRoots =
        ["com.positivegrid.spark.model.Fx*", "com.positivegrid.spark.DataBinderMapperImpl", "com.positivegrid.controls.Knob"];
    private const int CatalogDepth = 2;

    /// <summary>
    /// Decompiles <paramref name="apk"/> into <paramref name="output"/>: its resources, and of its 22,000 classes the
    /// thousand the catalog reads, reporting classes done of all (FlintDecompile.java). Should that fail, say on a jadx
    /// whose API has moved on, jadx's own command decompiles every class instead: the same files, in a minute, not seconds.
    /// </summary>
    public static async Task DecompileAsync(string apk, string output, IProgress<(int Done, int Total)> progress, CancellationToken cancel)
    {
        try
        {
            var helper = Path.Combine(SetupPaths.Tools, "jadx", "flint");
            Directory.CreateDirectory(helper);
            await using (var cls = typeof(Jadx).Assembly.GetManifestResourceStream("FlintDecompile.class")!)
            await using (var file = File.Create(Path.Combine(helper, "FlintDecompile.class")))
                await cls.CopyToAsync(file, cancel);
            var jar = Directory.EnumerateFiles(Path.Combine(SetupPaths.Tools, "jadx", "lib"), "jadx-*-all.jar").FirstOrDefault()
                      ?? throw new IOException("jadx has no jadx-*-all.jar");
            var (code, text) = await RunAsync(Java.Find() ?? "java",
                ["-Xmx3g", "-cp", jar + Path.PathSeparator + helper, "FlintDecompile", apk, output, Threads, CatalogDepth.ToString(), .. CatalogRoots],
                output, progress, cancel);
            if (code != 0) throw new IOException($"exit {code}: {CommandLine.Tail(text)}"); // it may have written part of it
        }
        catch (IOException e)
        {
            Log.Warn("setup", $"decompiling only what the catalog reads failed, so decompiling everything: {e.Message}");
            // Uncapped, it took 30 GB on a big machine; 3 GB and 4 threads take about 45 s.
            await RunAsync(Launcher, ["-d", output, "--show-bad-code", "--deobf-min", "2", "-j", Threads, apk], output, progress, cancel,
                new Dictionary<string, string> { ["JAVA_OPTS"] = "-Xmx3g" });
        }
    }

    private static string Threads => Math.Min(Environment.ProcessorCount, 4).ToString();

    /// <summary>
    /// jadx exits 3 when some of the app's classes do not decompile (about 150 of all of them do not), which the catalog
    /// never needs, so success is what it wrote, not its exit code.
    /// </summary>
    private static async Task<(int ExitCode, string Output)> RunAsync(string file, string[] args, string output, IProgress<(int Done, int Total)> progress,
                                                                      CancellationToken cancel, Dictionary<string, string>? environment = null)
    {
        if (Directory.Exists(output)) Directory.Delete(output, recursive: true);
        var (code, text) = await CommandLine.RunAsync(file, args, cancel, line =>
        {
            if (Progress(line) is { } p) progress.Report(p);
        }, environment);
        if (!File.Exists(Path.Combine(output, "resources", "AndroidManifest.xml")) || !Directory.Exists(Path.Combine(output, "sources")))
            throw new IOException($"jadx failed (exit {code}): {CommandLine.Tail(text)}");
        return (code, text);
    }

    /// <summary>jadx's <c>INFO  - progress: 3144 of 17803 (17%)</c>, or null for any other line.</summary>
    public static (int Done, int Total)? Progress(string line) =>
        ProgressLine().Match(line) is { Success: true } m ? (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value)) : null;

    [GeneratedRegex(@"^jadx-[0-9.]+\.zip$")]
    private static partial Regex ReleaseZip();

    [GeneratedRegex(@"progress: (\d+) of (\d+)")]
    private static partial Regex ProgressLine();
}
