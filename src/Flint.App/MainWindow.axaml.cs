using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Flint.App.Assets;
using Flint.App.Connect;
using Flint.App.Controls;
using Flint.App.Menu;
using Flint.App.Music;
using Flint.App.Setup;
using Flint.App.Tone;
using Flint.App.Tuner;
using Flint.Core.Connection;
using Flint.Core.Library;
using Flint.Core.Logging;
using Flint.Core.Model;

namespace Flint.App;

/// <summary>
/// The window: the tab's screen with the app's tab bar under it, the link state below that, and the sync question and
/// warnings above.
/// Every redraw reads the <see cref="AmpController"/> afresh.
/// </summary>
public partial class MainWindow : Window
{
    private static readonly TimeSpan BannerTime = TimeSpan.FromSeconds(10);
    private static readonly IBrush Green = Brush.Parse("#3fb950"), Amber = Brush.Parse("#d29922"), Red = Brush.Parse("#f85149");

    private readonly AmpController _amp;
    private readonly ToneScreen? _tone;
    private readonly MenuScreen? _menu;
    private readonly MusicScreen? _music;
    private readonly BottomNav? _tabs;
    private readonly ConnectScreen? _connect;
    private readonly AssetStore? _assets;
    private readonly AndroidValues? _values;
    private AppTab _tab = AppTab.Tone;
    private TunerScreen? _tuner;
    private bool _tunerBusy;
    private readonly Func<bool> _searching;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _bannerTimer = new() { Interval = BannerTime };
    private DateTime _backoffSince;
    private LinkState _link;
    private string? _error, _bannerShown;
    private readonly HashSet<string> _running = [];

    public MainWindow() : this(null!, null) // for the XAML previewer only
    {
    }

    /// <param name="searching">True while no amp is known yet, so connecting means scanning for one.</param>
    internal MainWindow(AmpController amp, AssetStore? assets, Func<bool>? searching = null)
    {
        _searching = searching ?? (() => false);
        InitializeComponent();
        _amp = amp;
        if (assets is not null)
        {
            FontFamily = SparkFonts.Raleway;
            var values = _values = new AndroidValues(Path.Combine(assets.Root, "res"));
            _assets = assets;
            _tone = new ToneScreen(assets, values, amp, Run);
            var rows = new MenuRows(assets, values);
            _menu = new MenuScreen(rows, amp, Run, OpenTuner);
            _music = new MusicScreen(rows, amp, Run);
            _tabs = new BottomNav(assets, values);
            _tabs.Chosen += tab =>
            {
                if (tab == _tab) Tab(tab)?.Home(); // tapping the current tab goes back to its top
                _tab = tab;
                Render();
            };
            TabBar.Child = _tabs;
            _connect = new ConnectScreen(assets, values);
            _connect.ButtonPressed += () =>
            {
                if (_amp.Link == LinkState.Idle) _amp.Start();
                else Run("stop connecting", _amp.StopAsync);
            };
        }
        RestoreAll.Click += (_, _) => Resolve(_amp.Pending?.Conflicts.Select(c => c.Slot) ?? [], restoreMine: true);
        KeepAll.Click += (_, _) => Resolve(_amp.Pending?.Conflicts.Select(c => c.Slot) ?? [], restoreMine: false);
        _bannerTimer.Tick += (_, _) => ClearBanner();
        _clock.Tick += (_, _) => Render(); // the backoff countdown
        _clock.Start();
        var barScale = new ScaleTransform(1, 1);
        BottomBar.LayoutTransform = TabBar.LayoutTransform = barScale;
        SizeChanged += (_, e) => barScale.ScaleX = barScale.ScaleY = ToneScreen.Scale(e.NewSize.Height); // grows with the screen
        _amp.Changed += () => Dispatcher.UIThread.Post(Render);
        Render();
    }

    private void Render()
    {
        if (_amp.Link != _link && _amp.Link == LinkState.Backoff) _backoffSince = DateTime.UtcNow;
        _link = _amp.Link;
        var state = _amp.State;
        var ready = _link == LinkState.Ready && _running.Count == 0;

        (StatusDot.Fill, StatusText.Text) = Status(state);
        // The connecting screen until the first sync, or after Cancel; the Tone screen from then on, even while
        // reconnecting, when the bar below says so and the last state stays on screen.
        var connecting = state is null || _link == LinkState.Idle;
        if (_tone is not null) ToneHost.Content = connecting ? _connect : Screen();
        BottomBar.IsVisible = !connecting || _tone is null;
        TabBar.IsVisible = !connecting && _tabs is not null;
        _tabs?.Show(_tab);
        Tab(_tab)?.Show();
        RenderTuner(state);
        _connect?.Show(_link, _amp.LastError, RetryRemaining(), _searching());
        _tone?.Show(state);
        RenderSync(_amp.Pending, ready);
        RenderBanner();
    }

    /// <summary>
    /// The setup screen over everything: on a first run, which has no artwork to draw the app with, or from the tray to
    /// update it. Once the artwork is in place, the app restarts to use it.
    /// </summary>
    internal void ShowSetup()
    {
        if (SetupHost.Content is SetupScreen) return;
        var setup = new SetupScreen(SetupPaths.Assets(_assets), update: _assets is not null);
        setup.Closed += () => (SetupHost.Content, SetupHost.IsVisible) = (null, false);
        setup.Restart += () => (Avalonia.Application.Current as App)?.Restart();
        (SetupHost.Content, SetupHost.IsVisible) = (setup, true);
        setup.Start();
    }

    private Control? Screen() => (Control?)Tab(_tab) ?? _tone;

    private PageStack? Tab(AppTab tab) => tab switch
    {
        AppTab.Menu => _menu,
        AppTab.Music => _music,
        _ => null,
    };

    /// <summary>The Menu tab's Tuner row: the tuner opens at once, and the amp's tuner goes on.</summary>
    private void OpenTuner() => SwitchTuner(true);

    /// <summary>
    /// Opens or closes the tuner and switches the amp's tuner to match. While that request is out the tuner stays as
    /// asked; after it, the tuner follows the amp, whose own button opens and closes it too.
    /// </summary>
    private void SwitchTuner(bool on)
    {
        _tunerBusy = true;
        ShowTuner(on);
        Run(on ? "turn the tuner on" : "turn the tuner off", async () =>
        {
            try
            {
                await _amp.SetTunerAsync(on);
            }
            finally
            {
                _tunerBusy = false;
            }
        });
    }

    /// <summary>The amp's tuner button: the app opens the tuner and answers with its own <c>01 65</c>, so this does too.</summary>
    private void RenderTuner(AmpState? state)
    {
        var on = state?.TunerOn == true && _link == LinkState.Ready;
        if (!_tunerBusy && on != (_tuner is not null))
        {
            if (on) SwitchTuner(true);
            else ShowTuner(false);
        }
        _tuner?.Show(state);
    }

    private void ShowTuner(bool on)
    {
        if (on == (_tuner is not null) || _connect is null) return;
        if (on)
        {
            _tuner = new TunerScreen(_assets!, _values!);
            _tuner.Closed += () => SwitchTuner(false);
        }
        else
        {
            _tuner = null;
        }
        Overlay.Content = _tuner;
        Overlay.IsVisible = on;
    }

    /// <summary>Green when connected; amber while connecting or reconnecting; red when this run has never reached the amp.</summary>
    private (IBrush, string) Status(AmpState? state)
    {
        var retry = Math.Ceiling(RetryRemaining().TotalSeconds);
        return _link switch
        {
            LinkState.Ready => (Green, $"Connected to {state!.Name}, firmware {state.Firmware}"),
            LinkState.Connecting => (Amber, "Connecting…"),
            LinkState.Syncing => (Amber, "Reading the amp…"),
            LinkState.Backoff when state is null => (Red, $"{_amp.LastError}. Trying again in {retry} s"),
            LinkState.Backoff => (Amber, $"Connection lost ({_amp.LastError}). Reconnecting in {retry} s"),
            _ => (Amber, "Starting…"),
        };
    }

    private TimeSpan RetryRemaining()
    {
        var left = _amp.RetryIn - (DateTime.UtcNow - _backoffSince);
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    /// <summary>The decision-9 question: per slot and for all of them, with the factory-reset wording when it looks like one.</summary>
    private void RenderSync(SyncResult? pending, bool ready)
    {
        SyncPanel.IsVisible = pending is not null;
        if (pending is null) return;
        var count = pending.Conflicts.Count;
        SyncTitle.Text = pending.FactoryReset
            ? count == AmpState.SlotCount ? "Your amp looks factory reset: all 8 presets were replaced." : "Your amp looks factory reset."
            : $"{count} {(count == 1 ? "slot differs" : "slots differ")} from your library, maybe changed in the phone app.";
        RestoreAll.Content = pending.FactoryReset ? $"Restore all {count} presets" : "Restore all mine";
        RestoreAll.IsEnabled = KeepAll.IsEnabled = ready;

        SyncRows.Children.Clear();
        foreach (var conflict in pending.Conflicts)
        {
            var restore = new Button { Content = "Restore mine", IsEnabled = ready };
            var keep = new Button { Content = "Keep the amp's", IsEnabled = ready };
            restore.Click += (_, _) => Resolve([conflict.Slot], restoreMine: true);
            keep.Click += (_, _) => Resolve([conflict.Slot], restoreMine: false);
            SyncRows.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"{SlotLabel(conflict.Slot)}:  yours \"{conflict.Mine.Name}\", the amp's \"{conflict.Amps.Name}\"",
                        VerticalAlignment = VerticalAlignment.Center, Width = 560,
                    },
                    restore, keep,
                },
            });
        }
    }

    /// <summary>The amp-wins warning (decision 7) or a failed action, shown for <see cref="BannerTime"/>.</summary>
    private void RenderBanner()
    {
        var text = _amp.Warning ?? _error;
        WarningBar.IsVisible = text is not null;
        WarningText.Text = text;
        if (text == _bannerShown) return;
        _bannerShown = text;
        _bannerTimer.Stop();
        if (text is not null) _bannerTimer.Start();
    }

    private void ClearBanner()
    {
        _bannerTimer.Stop();
        _error = null;
        if (_amp.Warning is not null) _amp.DismissWarning();
        Render();
    }

    private void Resolve(IEnumerable<int> slots, bool restoreMine) =>
        Run(restoreMine ? "restore" : "keep the amp's", () => _amp.ResolveAsync([.. slots], restoreMine));

    /// <summary>
    /// Runs an amp action. The same action twice at once (a double click) runs once, so nothing uploads twice; different
    /// actions all run, in order, because the session sends one request at a time (a second pick right after the first
    /// was dropped when this guard covered every action). A failure is logged and shown, never thrown.
    /// </summary>
    private async void Run(string what, Func<Task> action)
    {
        if (!_running.Add(what)) return;
        Render();
        try
        {
            await action();
        }
        catch (Exception e)
        {
            Log.Warn("ui", $"{what} failed: {e.Message}");
            _error = $"Could not {what}: {e.Message}";
        }
        finally
        {
            _running.Remove(what);
            Render();
        }
    }

    /// <summary>The app's own label for a slot ("CH1A"), as under the preset name.</summary>
    private static string SlotLabel(int slot) => ToneScreen.Channel(slot);
}
