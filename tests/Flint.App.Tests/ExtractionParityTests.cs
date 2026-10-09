using System.Diagnostics;
using System.Text.Json.Nodes;
using Flint.App.Setup;
using Xunit.Abstractions;

namespace Flint.App.Tests;

// The setup screen's extraction (AssetExtractor, CatalogBuilder) is a C# twin of scripts/extract-assets.sh and
// extract-catalog.py. On the owner's research/ (a decompiled Spark app), both run and must give the same assets: the
// same files with the same bytes, and the same catalog. Passes with a note where there is no research/ or no python3.
public sealed class ExtractionParityTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _temp = Directory.CreateTempSubdirectory("flint-parity-").FullName;

    public void Dispose() => Directory.Delete(_temp, recursive: true);

    [Fact]
    public void The_app_and_the_scripts_extract_the_same_assets()
    {
        var repo = Repo();
        var research = Path.Combine(repo, "research");
        if (!File.Exists(Path.Combine(research, "decompiled", "resources", "AndroidManifest.xml")) || !OnPath("python3"))
        {
            output.WriteLine("no decompiled app in research/, or no python3: nothing to compare");
            return;
        }

        // The script works on research/ and assets/ beside its own folder, so give it a copy of scripts/ in a folder of its own.
        Directory.CreateSymbolicLink(Path.Combine(_temp, "research"), research);
        Directory.CreateDirectory(Path.Combine(_temp, "scripts"));
        foreach (var file in Directory.EnumerateFiles(Path.Combine(repo, "scripts"))) File.Copy(file, Path.Combine(_temp, "scripts", Path.GetFileName(file)));
        var script = Run("bash", Path.Combine(_temp, "scripts", "extract-assets.sh"));
        var summary = AssetExtractor.Extract(Path.Combine(research, "decompiled"), Path.Combine(research, "extracted"), Path.Combine(_temp, "app"));

        Assert.Contains(summary, script);
        var (byScript, byApp) = (Path.Combine(_temp, "assets"), Path.Combine(_temp, "app"));
        var files = Files(byScript);
        Assert.Equal(files, Files(byApp));
        foreach (var file in files.Where(f => f is not "catalog.json" and not "manifest.json"))
            Assert.True(File.ReadAllBytes(Path.Combine(byScript, file)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(byApp, file))), $"{file} differs");
        Assert.True(JsonNode.DeepEquals(Json(byScript, "catalog.json"), Json(byApp, "catalog.json")), "catalog.json differs");
        foreach (var key in new[] { "app", "version" })
            Assert.Equal((string?)Json(byScript, "manifest.json")[key], (string?)Json(byApp, "manifest.json")[key]);
        output.WriteLine($"{files.Count} files the same; {summary}");
    }

    private static List<string> Files(string root) =>
        [.. Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(root, f)).Order(StringComparer.Ordinal)];

    private static JsonNode Json(string root, string file) => JsonNode.Parse(File.ReadAllText(Path.Combine(root, file)))!;

    private static string Run(string file, string argument)
    {
        using var process = Process.Start(new ProcessStartInfo(file, [argument]) { RedirectStandardOutput = true, RedirectStandardError = true })!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"{argument} failed: {stderr}");
        return stdout.Result;
    }

    private static bool OnPath(string tool) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':').Any(d => File.Exists(Path.Combine(d, tool)));

    private static string Repo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(dir.FullName, "Flint.slnx"))) dir = dir.Parent!;
        return dir.FullName;
    }
}
