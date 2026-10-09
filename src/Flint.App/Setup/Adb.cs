using System.Text.RegularExpressions;

namespace Flint.App.Setup;

/// <summary>
/// The Android Debug Bridge, which copies the Spark app off the user's phone. The system's adb when there is one,
/// else Google's platform-tools, downloaded into the tools folder.
/// </summary>
internal sealed partial class Adb(string path)
{
    public const string SparkPackage = "com.positivegrid.spark";

    private static readonly Uri PlatformTools =
        new($"https://dl.google.com/android/repository/platform-tools-latest-{(OperatingSystem.IsWindows() ? "windows" : "linux")}.zip");

    private static string Downloaded => Path.Combine(SetupPaths.Tools, "platform-tools", CommandLine.Exe("adb"));

    public string Executable { get; } = path;

    /// <summary>The system's adb or the one downloaded before, or null.</summary>
    public static Adb? Find() => (CommandLine.OnPath("adb") ?? (File.Exists(Downloaded) ? Downloaded : null)) is { } path ? new Adb(path) : null;

    public static async Task<Adb> InstallAsync(IProgress<double> progress, CancellationToken cancel)
    {
        await Download.ZipAsync(PlatformTools, SetupPaths.Tools, progress, cancel);
        return Find() ?? throw new IOException("the platform-tools download has no adb");
    }

    public async Task<Phone> PhoneAsync(CancellationToken cancel) => Phone.Parse((await RunAsync(["devices", "-l"], cancel)).Output);

    /// <summary>The paths of the Spark app's APKs on the phone (the base and the splits Google Play installed), empty when it is not installed.</summary>
    public async Task<List<string>> SparkApksAsync(Phone phone, CancellationToken cancel) =>
        [.. (await RunAsync(["-s", phone.Serial!, "shell", "pm", "path", SparkPackage], cancel)).Output
            .Split('\n', StringSplitOptions.TrimEntries).Where(l => l.StartsWith("package:")).Select(l => l["package:".Length..])];

    /// <summary>The Spark app's version on the phone ("4.6.2.10098"), or null.</summary>
    public async Task<string?> SparkVersionAsync(Phone phone, CancellationToken cancel) =>
        VersionName().Match((await RunAsync(["-s", phone.Serial!, "shell", "dumpsys", "package", SparkPackage], cancel)).Output) is { Success: true } m
            ? m.Groups[1].Value
            : null;

    /// <summary>
    /// Copies files off the phone into <paramref name="folder"/>, reporting the share of all their bytes copied (0 to 1),
    /// read from the copies' sizes as they grow.
    /// </summary>
    public async Task PullAsync(Phone phone, IReadOnlyList<string> files, string folder, IProgress<double> progress, CancellationToken cancel)
    {
        Directory.CreateDirectory(folder);
        var sizes = new List<long>();
        foreach (var file in files)
            sizes.Add(long.TryParse((await RunAsync(["-s", phone.Serial!, "shell", "stat", "-c", "%s", file], cancel)).Output.Trim(), out var size) ? size : 0);
        var total = Math.Max(1, sizes.Sum());
        long done = 0;
        for (var i = 0; i < files.Count; i++)
        {
            var target = Path.Combine(folder, Path.GetFileName(files[i]));
            var pull = RunAsync(["-s", phone.Serial!, "pull", files[i], target], cancel);
            while (await Task.WhenAny(pull, Task.Delay(250, cancel)) != pull)
                progress.Report((done + (File.Exists(target) ? new FileInfo(target).Length : 0)) / (double)total);
            var (code, output) = await pull;
            if (code != 0) throw new IOException($"could not copy {Path.GetFileName(files[i])} from the phone: {CommandLine.Tail(output)}");
            done += sizes[i];
            progress.Report(done / (double)total);
        }
    }

    private Task<(int ExitCode, string Output)> RunAsync(string[] args, CancellationToken cancel) => CommandLine.RunAsync(Executable, args, cancel);

    [GeneratedRegex(@"versionName=(\S+)")]
    private static partial Regex VersionName();
}
