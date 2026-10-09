using System.IO.Compression;
using Flint.Core.Logging;

namespace Flint.App.Setup;

/// <summary>
/// Takes the Spark app's artwork from the user's phone (or an .apkm file) into the assets folder: Java, adb, the phone,
/// the Spark app on it, the copy, jadx, the extraction. Each step says what it is doing; a step that needs the user
/// says what to do and checks again by itself until it is done.
/// </summary>
internal sealed class SetupFlow(string target, string? apkm = null)
{
    private const int JavaStep = 0;
    private static readonly TimeSpan Poll = TimeSpan.FromSeconds(1.5);

    private readonly List<SetupStep> _steps = apkm is null
        ? [new("Java"), new("Android tools (adb)"), new("Your phone"), new("The Spark app on your phone"), new("Copying the Spark app"),
           new("Unpacking the Spark app"), new("Taking the artwork")]
        : [new("Java"), new($"Opening {Path.GetFileName(apkm)}"), new("Unpacking the Spark app"), new("Taking the artwork")];

    private int _current;

    /// <summary>Raised on whatever thread made the change.</summary>
    public event Action? Changed;

    public IReadOnlyList<SetupStep> Steps => _steps;

    public bool Finished { get; private set; }

    /// <summary>Runs to the end, or to the first failure, which the failed step shows. Never throws but for cancelling.</summary>
    public async Task RunAsync(CancellationToken cancel)
    {
        try
        {
            if (Directory.Exists(SetupPaths.Work)) Directory.Delete(SetupPaths.Work, recursive: true);
            await JavaAsync(cancel);
            if (apkm is null)
            {
                var adb = await AdbAsync(cancel);
                var (phone, apks) = await PhoneAndAppAsync(adb, cancel);
                await CopyAsync(adb, phone, apks, cancel);
            }
            else
            {
                await OpenFileAsync(apkm, cancel);
            }
            await DecompileAsync(cancel);
            await ExtractAsync(cancel);
            Directory.Delete(SetupPaths.Work, recursive: true); // the APKs and the decompiled code: only the assets stay
            Finished = true;
            Log.Info("setup", $"done: assets in {target}");
            Changed?.Invoke();
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            Log.Info("setup", "cancelled");
        }
        catch (Exception e)
        {
            Log.Error("setup", $"{_steps[_current].Title} failed", e);
            Set(StepState.Failed, e.Message.Length > 0 ? char.ToUpperInvariant(e.Message[0]) + e.Message[1..] : e.GetType().Name);
        }
    }

    private async Task JavaAsync(CancellationToken cancel)
    {
        Start(JavaStep, "Looking for Java");
        while (true)
        {
            var java = Java.Find();
            var major = java is null ? null : Java.Major((await CommandLine.RunAsync(java, ["-version"], cancel)).Output);
            if (major >= Java.Needed)
            {
                Set(StepState.Done, $"Java {major}");
                return;
            }
            var command = Java.InstallCommand();
            var missing = major is null ? "Unpacking the Spark app needs Java, and this computer has none."
                                        : $"Java {major} is installed, but unpacking the Spark app needs Java {Java.Needed} or newer.";
            Set(StepState.NeedsYou, missing + (command is null
                ? " Install Java 17 or newer (from adoptium.net, or your distribution's packages). Setup carries on once it's there."
                : " Install it by running this in a terminal. Setup carries on once it's there."), command: command);
            await Task.Delay(Poll, cancel);
        }
    }

    private async Task<Adb> AdbAsync(CancellationToken cancel)
    {
        Start(1, "Looking for adb");
        var adb = Adb.Find();
        if (adb is null)
        {
            Set(StepState.Running, "Downloading Google's Android platform tools", 0);
            adb = await Adb.InstallAsync(Bar(p => $"Downloading Google's Android platform tools: {p:P0}"), cancel);
        }
        Set(StepState.Done, adb.Executable);
        return adb;
    }

    /// <summary>Waits for one phone, ready, with the Spark app on it, explaining whatever stands in the way.</summary>
    private async Task<(Phone, List<string>)> PhoneAndAppAsync(Adb adb, CancellationToken cancel)
    {
        while (true)
        {
            Begin(2, "Looking for your phone");
            var phone = await adb.PhoneAsync(cancel);
            if (phone.State != PhoneState.Ready)
            {
                var command = phone.State == PhoneState.NoPermissions ? Phone.UdevCommand(Java.OsRelease()) : null;
                Set(StepState.NeedsYou, Explain(phone.State, command is not null), command: command);
                Reset(3);
                await Task.Delay(Poll, cancel);
                continue;
            }
            Set(StepState.Done, $"{phone.Model ?? "A phone"} is connected");

            Begin(3, "Looking for the Spark app");
            var apks = await adb.SparkApksAsync(phone, cancel);
            if (apks.Count > 0)
            {
                Set(StepState.Done, $"Spark {await adb.SparkVersionAsync(phone, cancel) ?? ""}".Trim());
                return (phone, apks);
            }
            Set(StepState.NeedsYou, "Spark isn't installed on this phone. Install it from Google Play; setup carries on once it's there.");
            await Task.Delay(Poll, cancel);
        }
    }

    private static string Explain(PhoneState state, bool hasCommand) => state switch
    {
        PhoneState.Unauthorized => "Your phone is asking whether to allow USB debugging from this computer. Unlock it and tap Allow.",
        PhoneState.NoPermissions => "Linux isn't letting Flint reach the phone: it needs Android's USB rules. " +
                                    (hasCommand ? "Install them by running this in a terminal, " : "Install your distribution's Android udev rules (often called android-udev or android-tools), ") +
                                    "then unplug the phone and plug it in again.",
        PhoneState.Offline => "The phone is connected but isn't answering. Unplug it and plug it in again.",
        PhoneState.Several => "More than one Android device is connected. Unplug all but the phone with the Spark app.",
        _ => "Connect your Android phone to this computer with a USB cable, with USB debugging on:\n" +
             "1. In Settings → About phone, tap Build number seven times.\n" +
             "2. In Settings → System → Developer options, turn on USB debugging.\n" +
             "Some cables only charge: if nothing happens, try another." +
             (OperatingSystem.IsWindows() ? " Some phones also need their maker's USB driver on Windows (Google's, for a Pixel)." : ""),
    };

    private async Task CopyAsync(Adb adb, Phone phone, List<string> apks, CancellationToken cancel)
    {
        Start(4, $"Copying {apks.Count} files", 0);
        await adb.PullAsync(phone, apks, SetupPaths.Apks, Bar(p => $"Copying {apks.Count} files: {p:P0}"), cancel);
        var bytes = Directory.EnumerateFiles(SetupPaths.Apks).Sum(f => new FileInfo(f).Length);
        Set(StepState.Done, $"{apks.Count} files, {bytes / 1e6:F0} MB");
    }

    private async Task OpenFileAsync(string file, CancellationToken cancel)
    {
        Start(1, "Unzipping");
        await Task.Run(() => ZipFile.ExtractToDirectory(file, SetupPaths.Apks, overwriteFiles: true), cancel);
        if (!File.Exists(Path.Combine(SetupPaths.Apks, "base.apk")))
            throw new InvalidDataException($"{Path.GetFileName(file)} is not an app bundle: it has no base.apk");
        Set(StepState.Done, $"{Directory.EnumerateFiles(SetupPaths.Apks, "*.apk").Count()} parts");
    }

    private async Task DecompileAsync(CancellationToken cancel)
    {
        Start(_steps.Count - 2, "Starting");
        if (!Jadx.Installed)
        {
            Set(StepState.Running, "Downloading jadx, which unpacks it", 0);
            await Jadx.InstallAsync(Bar(p => $"Downloading jadx, which unpacks it: {p:P0}"), cancel);
        }
        Set(StepState.Running, "Unpacking", 0);
        var step = _current;
        var parts = new Progress<(int Done, int Total)>(p => Update(step, $"Unpacking: {p.Done:N0} of {p.Total:N0} parts", (double)p.Done / p.Total));
        await Jadx.DecompileAsync(Path.Combine(SetupPaths.Apks, "base.apk"), SetupPaths.Decompiled, parts, cancel);
        Set(StepState.Done, "Unpacked");
    }

    private async Task ExtractAsync(CancellationToken cancel)
    {
        Start(_steps.Count - 1, "Starting");
        var step = _current;
        var summary = await Task.Run(() => AssetExtractor.Extract(SetupPaths.Decompiled, SetupPaths.Apks, target,
            new Progress<string>(text => Update(step, text, null))), cancel);
        Log.Info("setup", summary);
        Set(StepState.Done, "Done");
    }

    private IProgress<double> Bar(Func<double, string> detail)
    {
        var step = _current;
        return new Progress<double>(p => Update(step, detail(p), p));
    }

    /// <summary>A progress report, which arrives later than it was made: it must not undo the step's end.</summary>
    private void Update(int step, string detail, double? progress)
    {
        if (step == _current && _steps[step].State == StepState.Running) Set(StepState.Running, detail, progress);
    }

    private void Start(int step, string detail, double? progress = null)
    {
        _current = step;
        Set(StepState.Running, detail, progress);
    }

    /// <summary>Like <see cref="Start"/>, but a step already showing what the user must do, or done, keeps showing it while it is checked again.</summary>
    private void Begin(int step, string detail)
    {
        _current = step;
        if (_steps[step].State is not (StepState.NeedsYou or StepState.Done)) Set(StepState.Running, detail);
    }

    private void Reset(int step)
    {
        lock (_steps) _steps[step] = _steps[step] with { State = StepState.Waiting, Detail = "", Progress = null, Command = null };
    }

    /// <summary>Under a lock, Changed too: progress reports may come on several threads at once, where there is no UI thread.</summary>
    private void Set(StepState state, string detail, double? progress = null, string? command = null)
    {
        lock (_steps)
        {
            _steps[_current] = _steps[_current] with { State = state, Detail = detail, Progress = progress, Command = command };
            Changed?.Invoke();
        }
    }
}
