using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Threading;
using Flint.Core.Library;
using Flint.Core.Logging;

namespace Flint.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var moved = LegacyData.Move(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create),
                                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create),
                                    PresetLibrary.DefaultRoot);
        Log.Start(echo: !Console.IsOutputRedirected);
        Log.Info("app", $"start v{typeof(Program).Assembly.GetName().Version?.ToString(3)} {RuntimeInformation.RuntimeIdentifier} .NET {Environment.Version}, logs in {Log.Directory}");
        if (moved is not null) Log.Info("app", moved);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("app", "crash", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => Log.Error("app", "unobserved task failure", e.Exception);

        try
        {
            int code;
            using (var instance = SingleInstance.Claim(PresetLibrary.DefaultRoot))
            {
                if (instance is null) return 0;
                instance.Activated += () => Dispatcher.UIThread.Post(() => (Application.Current as App)?.Tray?.ShowWindow());
                code = BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
                if (Application.Current is App { Amp: { } amp }) Release(amp);
            }
            Log.Info("app", $"exit {code}");
            // Only once this instance has let go of the lock, or the new one would hand over to it and quit.
            if (Application.Current is App { Restarting: true })
                Process.Start(DesktopEntry.Command[0], [.. DesktopEntry.Command.Skip(1), .. args.Where(a => a != Autostart.Minimized)]);
            return code;
        }
        catch (Exception e)
        {
            Log.Error("app", "failed to start", e);
            return 1;
        }
    }

    /// <summary>
    /// Disconnects cleanly so the phone can take the amp, after the window has gone, so closing it is
    /// instant. Off the UI thread, whose dispatcher has stopped; at most 5 s, then the OS drops the link anyway.
    /// </summary>
    private static void Release(Core.Model.AmpController amp)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var done = Task.Run(() => amp.DisposeAsync().AsTask()).Wait(TimeSpan.FromSeconds(5));
        Log.Info("ble", done ? $"released the amp in {clock.ElapsedMilliseconds} ms" : "the amp was not released within 5 s");
    }

    // Named exactly this, and public, because the Avalonia XAML compiler looks it up by convention.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new X11PlatformOptions { WmClass = DesktopEntry.WmClass })
            .LogToTrace();
}
