using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Flint.App.Assets;
using Flint.App.Controls;
using Flint.App.Menu;
using Flint.Core.Model;
using Flint.Core.Protocol.Payloads;

namespace Flint.App.Music;

/// <summary>
/// The Simple Looper (<c>fragment_quick_looper.xml</c>, the app's QuickLooper page), driving the amp's own looper: the
/// length, tempo and count at the top (they open the settings while nothing is recorded), the ring with the big button
/// (record, stop, dub), undo/redo above it, and Discard, the state's button and the mixer below. The screen follows the
/// amp's reports (<see cref="Looper"/>); the count-in and recording progress are timed here from the tempo, as the app
/// times them, and the playback progress comes from the amp. The mixer (Bluetooth audio levels) is out of scope.
/// </summary>
internal sealed class LooperPage : Grid
{
    // The bright colour of the app's ring gradients for each state: red to record, cyan to play, orange to dub.
    private static readonly Color Red = Color.Parse("#d90c0c"), Cyan = Color.Parse("#00f2f5"), Orange = Color.Parse("#ff5c29");

    private readonly PageStack _stack;
    private readonly MenuRows _rows;
    private readonly AndroidValues _text;
    private readonly LooperRing _ring = new() { Width = 192, Height = 192 };
    private readonly Image _main, _undo, _discard;
    private readonly TextBlock _count, _length, _bpm, _beats, _bottomText;
    private readonly Border _bottom;
    private readonly Toast _toast = new();
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private Looper _looper = Looper.Initial;
    private DateTime _phaseSince = DateTime.UtcNow;

    private LooperPage(PageStack stack)
    {
        (_stack, _rows, _text) = (stack, stack.Rows, stack.Rows.Values);
        var white30 = _rows.Colour("white30pa");

        // The top bar: the arrow down closes the looper, which stops what runs, as the app does.
        var close = _rows.Picture("ic_arrow_down");
        close.Width = 56;
        close.HorizontalAlignment = HorizontalAlignment.Left;
        MenuRows.Tap(close, Close);
        var title = _rows.Text(_text.Text("music_looper_freemode"), 20, Brushes.White, semiBold: true, HorizontalAlignment.Center);
        var top = new Grid { Height = 44, Children = { title, close } };

        // Length, BPM and count: the settings, which open only while nothing is recorded.
        (_length, _bpm, _beats) = (Value(), Value(), Value());
        var settings = new Grid
        {
            Height = 72, ColumnDefinitions = new ColumnDefinitions("*,*,*"), Background = Brushes.Transparent,
            Children =
            {
                Setting(_length, "looper_timebar_length_desc", 0), Setting(_bpm, "looper_timebar_bpm_desc", 1), Setting(_beats, "looper_timebar_time_desc", 2),
            },
        };
        MenuRows.Tap(settings, OpenSettings);

        // The ring, the big button over it (a little low, as in the layout), the count-in number, and undo/redo above.
        _main = new Image { Width = 102, Height = 145, Stretch = Stretch.Uniform, Margin = new Thickness(0, 46, 0, 0) };
        MenuRows.Tap(_main, () => Press(LooperButton.Main));
        _count = _rows.Text("", 40, Brushes.White, semiBold: true, HorizontalAlignment.Center);
        _count.Margin = new Thickness(0, 0, 0, 16);
        _count.IsHitTestVisible = false;
        _undo = new Image { Width = 28, Height = 28, Margin = new Thickness(0, 0, 0, 10), VerticalAlignment = VerticalAlignment.Bottom };
        MenuRows.Tap(_undo, () => Press(LooperButton.UndoRedo));
        var ring = new Grid { Width = 192, Height = 192, Children = { _ring, _main, _count } };
        var middle = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto,*"),
            Children = { new Border { Child = _undo, [RowProperty] = 0 }, new Border { Child = ring, [RowProperty] = 1 } },
        };
        _undo.HorizontalAlignment = HorizontalAlignment.Center;

        // Discard, the state's button, and the mixer.
        // Borderless 96 × 40 buttons with their icons at their own size in the middle, as the app's are.
        _discard = new Image { Stretch = Stretch.None };
        var discard = ButtonArea(_discard, HorizontalAlignment.Left);
        MenuRows.Tap(discard, ConfirmDiscard);
        var mixer = ButtonArea(_rows.Picture("ic_mixer"), HorizontalAlignment.Right);
        mixer.Opacity = 0.35;
        _bottomText = _rows.Text("", 14, Brushes.White, semiBold: true, HorizontalAlignment.Center);
        _bottom = new Border { Width = 151, Height = 44, CornerRadius = new CornerRadius(22), Child = _bottomText, HorizontalAlignment = HorizontalAlignment.Center };
        MenuRows.Tap(_bottom, () => Press(LooperButton.Bottom));
        var bottom = new Grid { Height = 72, Children = { discard, mixer, _bottom } };

        Background = _rows.Colour("grey950");
        if (_rows.Assets.Image("bg_profile_header") is not null)
            Children.Add(new Image { Source = _rows.Assets.Bitmap("bg_profile_header"), Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Top });
        var page = new DockPanel();
        foreach (var (control, dock) in new (Control, Dock)[] { (top, Dock.Top), (settings, Dock.Top), (bottom, Dock.Bottom) })
        {
            DockPanel.SetDock(control, dock);
            page.Children.Add(control);
        }
        page.Children.Add(middle);
        Children.Add(page);
        Children.Add(_toast);

        _clock.Tick += (_, _) => Draw();
        AttachedToVisualTree += (_, _) => _clock.Start();
        DetachedFromVisualTree += (_, _) => _clock.Stop();

        static Border ButtonArea(Image icon, HorizontalAlignment side) =>
            new() { Width = 96, Height = 40, Background = Brushes.Transparent, HorizontalAlignment = side, Child = icon };

        TextBlock Value() => _rows.Text("--", 14, Brushes.White, semiBold: true, HorizontalAlignment.Center);
        StackPanel Setting(TextBlock value, string label, int column)
        {
            var description = _rows.Text(_text.Text(label), 14, white30, align: HorizontalAlignment.Center);
            description.Margin = new Thickness(0, 4, 0, 0);
            return new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { value, description }, [ColumnProperty] = column };
        }
    }

    /// <summary>The Simple Looper as a page of the Music tab. Opening it reads what the amp's looper is doing, as the app does.</summary>
    public static TabPage Create(PageStack stack)
    {
        LooperPage? page = null;
        stack.Run("read the looper", () => stack.Amp.ReadLooperAsync());
        return new TabPage(_ => "looper", _ => page = new LooperPage(stack), s => page?.Show(s.Looper));
    }

    /// <summary>The settings page's labels: a length in bars or Free, and a count of 3 as Shuffle and 4 as Straight.</summary>
    public static (string Length, string Beats) Labels(AndroidValues text, LooperTimeSignature timing) => (
        timing.FreeLength ? text.Text("looper_settings_looplengthmenu_free") : $"{timing.Bars} {text.Text("looper_settings_looplength_nbars")}",
        timing.Beats switch
        {
            3 => text.Text("looper_settings_timesig_shuffle"),
            4 => text.Text("looper_settings_timesig_straight"),
            var n => n.ToString(CultureInfo.InvariantCulture),
        });

    private void Show(Looper looper)
    {
        if (looper.Phase != _looper.Phase) _phaseSince = DateTime.UtcNow;
        _looper = looper;
        var timing = looper.Timing;
        if (timing is { CountIn: true })
        {
            (_length.Text, _beats.Text) = Labels(_text, timing);
            _bpm.Text = timing.Bpm.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            _length.Text = _bpm.Text = _beats.Text = "--"; // with the click off the app shows no time settings
        }
        if (looper.Phase == LooperPhase.Ready)
            _ring.Art = Art(timing is null || timing.FreeLength || !timing.CountIn ? "bg_looper_free" : $"bg_looper_{timing.Beats}_{timing.Bars:00}");

        _main.Source = Art(looper.Phase switch
        {
            LooperPhase.CountIn => "icon_looper_count_in",
            LooperPhase.StoppedCountIn => "icon_looper_ending_intro",
            LooperPhase.Recording => "btn_looper_stop_record",
            LooperPhase.Playing or LooperPhase.Stopped => "btn_looper_playing",
            LooperPhase.Dubbing => "btn_looper_dubbing",
            _ => "btn_looper_record",
        });
        _undo.Source = Art(looper.Undo switch { UndoState.Undo => "btn_undo", UndoState.Redo => "btn_redo", _ => "btn_undo_gray" });
        _discard.Source = Art(looper.Phase == LooperPhase.Stopped ? "ic_delete_remove" : "ic_delete_remove_grey");
        var (label, fill, colour) = looper.Phase switch
        {
            LooperPhase.Recording => ("looper_freemode_retry_btn", "spark_red_500", "white"),
            LooperPhase.Stopped => ("looper_play_btn", "spark_red_500", "white"),
            LooperPhase.Playing => ("looper_stop_btn", "white10pa", "white"),
            LooperPhase.Dubbing => ("looper_stop_btn", "white10pa", "white20pa"),
            _ => (null, null, null),
        };
        _bottom.IsVisible = label is not null;
        if (label is not null)
        {
            _bottomText.Text = _text.Text(label);
            _bottom.Background = _rows.Colour(fill!);
            _bottomText.Foreground = _rows.Colour(colour!);
        }
        Draw();
    }

    /// <summary>The ring's progress, the count-in number and the punch-in blink, redrawn on the clock.</summary>
    private void Draw()
    {
        var (phase, timing) = (_looper.Phase, _looper.Timing);
        var elapsed = (DateTime.UtcNow - _phaseSince).TotalSeconds;
        var beatSeconds = timing is { Bpm: > 0 } ? 60.0 / timing.Bpm : 0.5;
        var beats = (int)(timing?.Beats ?? 4);
        var loopBeats = timing is { CountIn: true } ? beats * (int)timing.Bars : 0;
        var colour = phase switch
        {
            LooperPhase.Playing or LooperPhase.Stopped or LooperPhase.StoppedCountIn => Cyan,
            LooperPhase.Dubbing => Orange,
            _ => Red,
        };
        var counting = phase is LooperPhase.CountIn or LooperPhase.StoppedCountIn;
        var progress = phase switch
        {
            _ when counting => elapsed / (beats * beatSeconds),
            LooperPhase.Recording when loopBeats > 0 => elapsed / (loopBeats * beatSeconds),
            LooperPhase.Recording => elapsed / Math.Max(1, timing?.MaxRecordingLength ?? 60),
            LooperPhase.Playing or LooperPhase.Dubbing => _looper.Progress,
            _ => 0,
        };
        _ring.Show(counting ? beats : loopBeats, progress, colour);
        _count.IsVisible = counting;
        _count.Text = Math.Clamp((int)Math.Ceiling(progress * beats), 1, beats).ToString(CultureInfo.InvariantCulture);
        _ring.Opacity = _main.Opacity = phase == LooperPhase.PunchIn ? 0.7 + 0.3 * Math.Cos(elapsed * Math.PI) : 1; // 1 → 0.4 → 1 every 2 s
    }

    private void Press(LooperButton button) => _stack.Run($"looper {button}", () => _stack.Amp.PressLooperAsync(button));

    private void Close()
    {
        Press(LooperButton.Close);
        _stack.Pop();
    }

    private void OpenSettings()
    {
        if (_looper.Phase == LooperPhase.Ready) _stack.Push(LooperSettingsPage.Create(_stack));
        else _toast.Show(_text.Text("looper_settings_playbackadj_error"));
    }

    /// <summary>Discard asks first, and only a stopped loop can be discarded.</summary>
    private void ConfirmDiscard()
    {
        if (_looper.Phase != LooperPhase.Stopped) return;
        Dialog? dialog = null;
        dialog = new Dialog(_rows, _text.Text("looper_discard_toast_title"), _text.Text("looper_discard_toast_desc"),
            _text.Text("looper_discard_toast_cancel_btn"), _text.Text("looper_discard_toast_discard_btn"), discard =>
            {
                Children.Remove(dialog!);
                if (discard) Press(LooperButton.Discard);
            });
        Children.Add(dialog);
    }

    private IImage? Art(string name) => VectorArt.Drawable(_rows.Assets, _text, name);
}
