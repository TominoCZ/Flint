using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Flint.App.Assets;
using Flint.Bluetooth.BlueZ;
#if WINDOWS
using Flint.Bluetooth.WinRT;
#endif
using Flint.Core.Connection;
using Flint.Core.Library;
using Flint.Core.Logging;
using Flint.Core.Model;

namespace Flint.App;

public partial class App : Application
{
    private AppSettings _settings = AppSettings.Load();
    private Func<string?>? _linkAddress;

    /// <summary>The amp, for <see cref="Program"/> to release once the window is gone.</summary>
    internal AmpController? Amp { get; private set; }

    /// <summary>The tray and the window, for <see cref="Program"/> to show the window when the app is launched again.</summary>
    internal Tray? Tray { get; private set; }

    /// <summary>Set when setup has new artwork in place: <see cref="Program"/> starts the app again on its way out, to use it.</summary>
    internal bool Restarting { get; private set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var amp = Amp = new AmpController(NewLink, new PresetLibrary(PresetLibrary.DefaultRoot));
            amp.Changed += () => RememberAmp(amp);
            var assets = AssetStore.Find();
            if (assets is not null) SparkFonts.Register(assets);
            var window = new MainWindow(amp, assets, () => _settings.Address is null);
            window.Icon = new WindowIcon(FlintIcon.Render(256));
            var icon = FlintIcon.Save();
            DesktopEntry.Install(icon);
            Autostart.Refresh(icon);
            Tray = new Tray(this, desktop, window, icon, window.ShowSetup);
            // In the tray only at login, and not before there is artwork: closing setup's window quits.
            _ = Tray.StartAsync(show: desktop.Args?.Contains(Autostart.Minimized) != true, tray: assets is not null);
            if (assets is null) window.ShowSetup();
            amp.Start();
        }
        base.OnFrameworkInitializationCompleted();
    }

    internal void Restart()
    {
        Log.Info("app", "restarting to use the new artwork");
        Restarting = true;
        (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }

    private ILink NewLink()
    {
#if WINDOWS
        var link = new WinRtLink(_settings.Address);
#else
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("this build has no Bluetooth link for this OS: Windows needs build-windows.bat's");
        var link = new BlueZLink(_settings.Address);
#endif
        _linkAddress = () => link.Address;
        return link;
    }

    /// <summary>The first run found the amp by name; every later run connects to that address straight away.</summary>
    private void RememberAmp(AmpController amp)
    {
        if (amp.Link != LinkState.Ready || _settings.Address is not null || _linkAddress?.Invoke() is not { } address) return;
        _settings = _settings with { Address = address };
        try
        {
            _settings.Save();
            Log.Info("app", $"remembered the amp at {address}");
        }
        catch (IOException e)
        {
            Log.Warn("app", $"settings.json not saved: {e.Message}");
        }
    }
}
