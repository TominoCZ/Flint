using Flint.App.Setup;
using Xunit.Abstractions;

namespace Flint.App.Tests;

// The whole setup, from a phone to assets, with a stand-in adb serving the owner's research/extracted/ as a phone that
// first waits to be allowed, then lacks udev rules, then is ready. Real java, jadx and extraction, into a temporary data
// folder. About 45 s and 3 GB, so it runs only with FLINT_E2E=1 (and research/ and Java present).
[Collection(Name)]
public sealed class SetupFlowTests(ITestOutputHelper output) : IDisposable
{
    public const string Name = "setup-flow"; // alone: it changes the process's PATH and XDG_DATA_HOME

    private readonly string _temp = Directory.CreateTempSubdirectory("flint-flow-").FullName;
    private readonly string? _path = Environment.GetEnvironmentVariable("PATH"), _data = Environment.GetEnvironmentVariable("XDG_DATA_HOME");

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PATH", _path);
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", _data);
        Directory.Delete(_temp, recursive: true);
    }

    [Fact]
    public async Task A_phone_that_needs_the_user_twice_ends_in_assets()
    {
        var research = Path.Combine(Repo(), "research");
        var apks = Path.Combine(research, "extracted");
        if (OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("FLINT_E2E") != "1" || !File.Exists(Path.Combine(apks, "base.apk")) || Java.Find() is null)
        {
            output.WriteLine("set FLINT_E2E=1, with research/extracted/ and Java present, to run the whole setup");
            return;
        }
        var bin = Directory.CreateDirectory(Path.Combine(_temp, "bin")).FullName;
        File.WriteAllText(Path.Combine(bin, "adb"), FakeAdb(apks, Path.Combine(_temp, "calls")));
        File.SetUnixFileMode(Path.Combine(bin, "adb"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Environment.SetEnvironmentVariable("PATH", bin + Path.PathSeparator + _path);
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", Path.Combine(_temp, "data"));
        Directory.CreateDirectory(SetupPaths.Tools);
        Directory.CreateSymbolicLink(Path.Combine(SetupPaths.Tools, "jadx"), Path.Combine(research, "tools", "jadx")); // no download

        var target = Path.Combine(_temp, "assets");
        var flow = new SetupFlow(target);
        var seen = new List<string>();
        flow.Changed += () =>
        {
            lock (seen) seen.AddRange(flow.Steps.Where(s => s.State != StepState.Waiting).Select(s => $"{s.Title}: {s.State}: {s.Detail}"));
        };
        await flow.RunAsync(CancellationToken.None);

        output.WriteLine(string.Join("\n", seen.Distinct()));
        Assert.True(flow.Finished, flow.Steps.FirstOrDefault(s => s.State == StepState.Failed)?.Detail);
        Assert.Contains(seen, s => s.StartsWith("Your phone: NeedsYou: Your phone is asking whether to allow USB debugging"));
        Assert.Contains(seen, s => s.StartsWith("Your phone: NeedsYou: Linux isn't letting Flint reach the phone"));
        Assert.Contains(seen, s => s.StartsWith("Your phone: Done: Pixel 7"));
        Assert.Contains(seen, s => s.StartsWith("Unpacking the Spark app: Running: Unpacking: ") && !s.Contains("Unpacking: 0 of"));
        Assert.True(File.Exists(Path.Combine(target, "catalog.json")) && File.Exists(Path.Combine(target, "manifest.json")));
        Assert.False(Directory.Exists(SetupPaths.Work)); // the APKs and the decompiled code are gone
    }

    /// <summary>adb as setup calls it: devices, pm path, dumpsys, stat, pull. The phone is a Pixel 7 holding the APKs in <paramref name="apks"/>.</summary>
    private static string FakeAdb(string apks, string calls) => $$"""
        #!/usr/bin/env bash
        [ "$1" = -s ] && shift 2
        case "$1" in
          devices)
            n=$(( $(cat {{calls}} 2>/dev/null || echo 0) + 1 )); echo $n > {{calls}}
            echo "List of devices attached"
            if [ $n -le 2 ]; then echo "PIXEL7SERIAL           unauthorized usb:1-2 transport_id:1"
            elif [ $n -le 4 ]; then echo "PIXEL7SERIAL           no permissions (missing udev rules? user is in the plugdev group); see [http://developer.android.com/tools/device.html] usb:1-2"
            else echo "PIXEL7SERIAL           device usb:1-2 product:panther model:Pixel_7 device:panther transport_id:1"; fi ;;
          shell)
            case "$2" in
              pm) for f in $(cd {{apks}} && ls *.apk); do printf 'package:/data/app/~~x==/com.positivegrid.spark-y==/%s\r\n' "$f"; done ;;
              dumpsys) echo "    versionCode=10098 minSdk=26 targetSdk=34"; echo "    versionName=4.6.2.10098" ;;
              stat) stat -c %s "{{apks}}/$(basename "$5")" ;;
            esac ;;
          pull) cp "{{apks}}/$(basename "$2")" "$3" ;;
        esac
        """;

    private static string Repo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(dir.FullName, "Flint.slnx"))) dir = dir.Parent!;
        return dir.FullName;
    }
}

[CollectionDefinition(SetupFlowTests.Name, DisableParallelization = true)]
public sealed class SetupFlowCollection;
