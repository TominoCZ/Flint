using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Flint.Core.Logging;

namespace Flint.App;

/// <summary>
/// The app in the tray, so the link stays up out of sight: the window starts hidden only at login, its close
/// button hides it, clicking the tray icon shows it, and the icon's menu offers Open and Quit. With no tray to come
/// back through (no StatusNotifier host, or no icon), the window shows at start and closing it quits. The menu also
/// holds "Start at login" (<see cref="Autostart"/>) and "Update Spark artwork…", which are ours, not the phone app's.
/// </summary>
internal sealed class Tray
{
    private readonly Application _app;
    private readonly IClassicDesktopStyleApplicationLifetime _desktop;
    private readonly Window _window;
    private readonly string? _iconPath;
    private readonly Action _updateArtwork;
    private TrayIcon? _icon;
    private bool _drawn;

    /// <param name="iconPath">The icon's file, for the autostart entry.</param>
    /// <param name="updateArtwork">Opens the setup screen, to take the artwork from a newer Spark app.</param>
    public Tray(Application app, IClassicDesktopStyleApplicationLifetime desktop, Window window, string? iconPath, Action updateArtwork)
    {
        (_app, _desktop, _window, _iconPath, _updateArtwork) = (app, desktop, window, iconPath, updateArtwork);
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown; // until Start knows whether there is a tray
        window.Closing += (_, e) =>
        {
            if (_icon is null || e.CloseReason != WindowCloseReason.WindowClosing) return;
            e.Cancel = true;
            window.Hide();
        };
    }

    /// <summary>Puts the icon in the tray and shows the window if <paramref name="show"/>, or, with no tray, shows the window anyway.</summary>
    public async Task StartAsync(bool show)
    {
        var available = await TrayHost.AvailableAsync();
        if (available && _window.Icon is { } icon)
        {
            _icon = NewIcon(icon);
            TrayIcon.SetIcons(_app, [_icon]);
            Log.Info("app", "started in the tray");
            if (show) ShowWindow();
            return;
        }
        Log.Warn("app", "no system tray (StatusNotifier host) on this desktop: closing the window quits");
        _desktop.MainWindow = _window;
        _desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
        ShowWindow();
    }

    /// <summary>Shows the window, restored and in front. The first time, the log says how long it took to lay out and draw.</summary>
    public void ShowWindow()
    {
        if (!_drawn)
        {
            _drawn = true;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            Dispatcher.UIThread.Post(() => Log.Info("app", $"window drawn in {clock.ElapsedMilliseconds} ms"), DispatcherPriority.Background);
        }
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private TrayIcon NewIcon(WindowIcon icon)
    {
        var open = new NativeMenuItem("Open");
        open.Click += (_, _) => ShowWindow();
        var quit = new NativeMenuItem("Quit");
        quit.Click += (_, _) => _desktop.Shutdown(); // Program.Release then lets go of the amp
        var login = new NativeMenuItem("Start at login") { ToggleType = MenuItemToggleType.CheckBox, IsChecked = Autostart.IsOn };
        login.Click += (_, _) =>
        {
            try
            {
                Autostart.Set(!Autostart.IsOn, _iconPath);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Warn("app", $"start at login not changed: {e.Message}");
            }
            login.IsChecked = Autostart.IsOn;
        };
        var update = new NativeMenuItem("Update Spark artwork…");
        update.Click += (_, _) =>
        {
            ShowWindow();
            _updateArtwork();
        };
        var tray = new TrayIcon { Icon = icon, ToolTipText = "Flint", Menu = [open, login, update, new NativeMenuItemSeparator(), quit] };
        tray.Clicked += (_, _) => ShowWindow();
        return tray;
    }
}
