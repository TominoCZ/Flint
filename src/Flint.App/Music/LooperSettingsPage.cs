using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Flint.App.Controls;
using Flint.App.Menu;
using Flint.Core.Model;
using Flint.Core.Protocol.Payloads;

namespace Flint.App.Music;

/// <summary>
/// The Simple Looper's settings (<c>fragment_looper_settings.xml</c>, <c>LooperSettingPage</c>): the tempo wheel, Click
/// (count-in and metronome), Loop Length (1–16 bars or Free) and Count (Shuffle is 3 beats, Straight 4), and Reset.
/// Each change goes to the amp at once as its time signature (<c>01 76</c>). The app's Ending row is the Groove
/// Looper's and is left out with it.
/// </summary>
internal static class LooperSettingsPage
{
    private static readonly uint[] Lengths = [1, 2, 4, 8, 12, 16, 0];
    private static readonly uint[] Counts = [3, 4];
    private static readonly LooperTimeSignature Default = new(120, 4, 4, false, true, false, 60); // what the owner's amp starts with

    public static TabPage Create(PageStack stack) => new(s => s.Looper.Timing ?? Default, s => Build(stack, s.Looper.Timing ?? Default));

    private static Control Build(PageStack stack, LooperTimeSignature timing)
    {
        var (rows, text) = (stack.Rows, stack.Rows.Values);
        var (bars, length) = (timing.FreeLength ? 0u : timing.Bars, LooperPage.Labels(text, timing));

        var close = rows.Picture("btn_close");
        (close.Width, close.HorizontalAlignment) = (56, HorizontalAlignment.Left);
        MenuRows.Tap(close, stack.Pop);
        var top = new Grid { Height = 52, Children = { rows.Text(text.Text("looper_settings_title"), 20, Brushes.White, semiBold: true, HorizontalAlignment.Center), close } };

        var wheel = new BpmWheel(rows.Assets, text, (int)timing.Bpm, (int)Looper.MinBpm(bars, timing.Beats, timing.MaxRecordingLength), 300)
        {
            Margin = new Thickness(0, 28, 0, 0), HorizontalAlignment = HorizontalAlignment.Center,
        };
        wheel.Changed += bpm => Send((uint)bpm, timing.CountIn, bars, timing.Beats);
        var tips = rows.Text(text.Text("looper_settings_bpm_desc"), 14, rows.Colour("white50pa"), align: HorizontalAlignment.Center);
        (tips.TextWrapping, tips.TextAlignment, tips.Margin) = (TextWrapping.Wrap, TextAlignment.Center, new Thickness(20));

        var reset = rows.Text(text.Text("looper_settings_reset_btn"), 14, Brushes.White, semiBold: true, HorizontalAlignment.Center);
        var resetButton = new Border
        {
            Width = 200, Height = 44, CornerRadius = new CornerRadius(22), BorderBrush = Brushes.White, BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 28, 0, 32), HorizontalAlignment = HorizontalAlignment.Center, Child = reset,
        };
        MenuRows.Tap(resetButton, () => Send(Default.Bpm, Default.CountIn, Default.Bars, Default.Beats));

        var page = new DockPanel { Background = rows.Colour("grey900") };
        DockPanel.SetDock(top, Dock.Top);
        page.Children.Add(top);
        page.Children.Add(PageStack.Scrolling(
            wheel, tips, Line(),
            rows.Switch(text.Text("looper_settings_click_title"), text.Text("looper_settings_click_desc"), timing.CountIn,
                () => Send(timing.Bpm, !timing.CountIn, bars, timing.Beats)),
            Line(),
            rows.Info(text.Text("looper_settings_looplength_title"), length.Length, open: () => stack.Push(Picker(stack, "looper_settings_looplengthmenu_title",
                Lengths, t => t.FreeLength ? 0 : t.Bars, (t, n) => Looper.TimingFor(t.Bpm, t.CountIn, n, t.Beats, t.MaxRecordingLength),
                n => LooperPage.Labels(text, Default with { Bars = n, FreeLength = n == 0 }).Length))),
            rows.Info(text.Text("looper_settings_timesig_title"), length.Beats, open: () => stack.Push(Picker(stack, "looper_settings_countmenu_title",
                Counts, t => t.Beats, (t, n) => Looper.TimingFor(t.Bpm, t.CountIn, t.FreeLength ? 0 : t.Bars, n, t.MaxRecordingLength),
                n => LooperPage.Labels(text, Default with { Beats = n }).Beats))),
            resetButton));
        return page;

        Control Line() => new Border { Height = 1, Background = rows.Colour("grey930"), Margin = new Thickness(0, 28, 0, 0) };

        void Send(uint bpm, bool click, uint barCount, uint beats)
        {
            var next = Looper.TimingFor(Math.Max(bpm, Looper.MinBpm(barCount, beats, timing.MaxRecordingLength)), click, barCount, beats, timing.MaxRecordingLength);
            stack.Run("set the looper", () => stack.Amp.SetLooperTimingAsync(next));
        }
    }

    /// <summary>Loop Length's and Count's lists: the choices, the current one ticked; a pick goes to the amp at once.</summary>
    private static TabPage Picker(PageStack stack, string title, uint[] choices, Func<LooperTimeSignature, uint> current,
        Func<LooperTimeSignature, uint, LooperTimeSignature> with, Func<uint, string> label) => new(s => s.Looper.Timing ?? Default, s =>
    {
        var timing = s.Looper.Timing ?? Default;
        var rows = new List<Control> { stack.Rows.TopBar(stack.Rows.Values.Text(title), stack.Pop) };
        foreach (var choice in choices)
            rows.Add(stack.Rows.Choice(label(choice), current(timing) == choice, enabled: true, () =>
            {
                var next = with(stack.Amp.State?.Looper.Timing ?? Default, choice);
                next = next with { Bpm = Math.Max(next.Bpm, Looper.MinBpm(next.FreeLength ? 0 : next.Bars, next.Beats, next.MaxRecordingLength)) };
                stack.Run("set the looper", () => stack.Amp.SetLooperTimingAsync(next));
            }));
        var page = new DockPanel { Background = stack.Rows.Colour("grey900") };
        DockPanel.SetDock(rows[0], Dock.Top);
        page.Children.Add(rows[0]);
        page.Children.Add(PageStack.Scrolling([.. rows.Skip(1)]));
        return page;
    });
}
