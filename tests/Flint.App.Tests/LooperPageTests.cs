using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Flint.App.Assets;
using Flint.Core.Connection;
using Flint.Core.Library;
using Flint.Core.Model;
using Flint.Core.Protocol;
using Flint.Core.Protocol.Payloads;
using Flint.Core.Tests;
using Xunit.Abstractions;

namespace Flint.App.Tests;

// The Music tab's Simple Looper over FakeAmp with the owner's assets/: its buttons send the app's looper actions, the
// amp's reports move it, and its settings reach the amp. Frames are saved as looper-*.png.
[Collection(Headless.Name)]
public sealed class LooperPageTests(Headless headless, ITestOutputHelper output) : IAsyncDisposable
{
    private static readonly AssetStore? Assets = AssetStore.Find();

    private readonly FakeAmp _amp = new();
    private readonly string _library = Directory.CreateTempSubdirectory("flint-looper-").FullName;
    private FakeLink? _link;
    private AmpController? _controller;
    private byte _seq = 0x40;

    public async ValueTask DisposeAsync()
    {
        if (_controller is not null) await Task.Run(() => _controller.DisposeAsync().AsTask());
        Directory.Delete(_library, recursive: true);
    }

    [Fact]
    public async Task The_simple_looper_records_plays_stops_discards_and_sets_its_length()
    {
        if (Assets is null) return;
        _controller = new AmpController(() => _link = new FakeLink { Respond = _amp.Respond }, new PresetLibrary(_library));
        _controller.Start();
        await Wait.Until(() => _controller.Link == LinkState.Ready && _controller.State is not null);

        await headless.Run(async () =>
        {
            SparkFonts.Register(Assets);
            var window = new MainWindow(_controller, Assets) { Width = 540, Height = 960 };
            window.Show();
            Pump();

            var tabs = window.GetVisualDescendants().OfType<Controls.BottomNav>().Single();
            Click(window, tabs.TranslatePoint(new Point(tabs.Bounds.Width * 0.3, tabs.Bounds.Height / 2), window)!.Value); // Music
            Save(window, "looper-music-tab");
            Tap(window, "Simple Looper");
            await Until(() => _controller.State!.Looper.Timing is not null); // read when the page opens
            Save(window, "looper-ready");

            var main = Looper(window).GetVisualDescendants().OfType<Image>().Single(i => i.Width == 102);
            Click(window, Centre(main, window));
            await Until(() => _amp.Looper.SequenceEqual([LooperAction.StartCountIn]));
            Report(LooperAction.StartCountIn);
            await Task.Delay(700);
            Save(window, "looper-count-in");
            Report(LooperAction.StartRecording);
            await Task.Delay(1500);
            Save(window, "looper-recording");
            Report(LooperAction.RecordingEnded);
            Report(LooperAction.PlaybackStarted);
            _link!.Send(new Chunk(_seq++, 0x03, 0x77, new FloatValue(0.4f).Encode()));
            await Until(() => _controller.State!.Looper.Progress > 0.3f);
            Save(window, "looper-playing");

            Tap(window, "Stop");
            await Until(() => _amp.Looper[^1] == LooperAction.PlaybackStopped);
            Report(LooperAction.PlaybackStopped);
            await Until(() => _controller.State!.Looper.Phase == LooperPhase.Stopped);
            Save(window, "looper-stopped");

            var discard = Looper(window).GetVisualDescendants().OfType<Border>().Single(b => b.Width == 96 && b.IsSet(InputElement.CursorProperty));
            Click(window, Centre(discard, window));
            Save(window, "looper-discard");
            Tap(window, "Discard");
            await Until(() => _amp.Looper[^1] == LooperAction.ClearPlayback);
            Report(LooperAction.ResetToReady);
            await Until(() => _controller.State!.Looper.Phase == LooperPhase.Ready);

            Tap(window, "BPM"); // the length, tempo and count open the settings while nothing is recorded
            Save(window, "looper-settings");
            Tap(window, "Loop Length");
            Tap(window, "8 Bars");
            await Until(() => _controller.State!.Looper.Timing!.Bars == 8);
            Assert.Equal(new LooperTimeSignature(120, 4, 8, false, true, false, 60), _controller.State!.Looper.Timing);
            Assert.Equal(4u, _amp.LooperTiming.Bars); // kept by Flint, never sent: the amp hung on 01 76
            window.Close();
        });
    }

    private static Control Looper(Window window) => window.GetVisualDescendants().OfType<Music.LooperPage>().Single();

    private void Report(LooperAction action) => _link!.Send(new Chunk(_seq++, 0x03, 0x75, new UIntValue((uint)action).Encode()));

    private static async Task Until(Func<bool> condition) => await Wait.Until(() =>
    {
        Pump();
        return condition();
    });

    private void Save(Window window, string name)
    {
        Pump();
        var file = Path.Combine(AppContext.BaseDirectory, name + ".png");
        window.CaptureRenderedFrame()!.Save(file, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        output.WriteLine($"rendered to {file}");
    }

    private static void Pump()
    {
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    private static Point Centre(Control c, Window window) => c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), window)!.Value;

    private static void Click(Window window, Point at)
    {
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Pump();
    }

    /// <summary>Clicks the control that takes clicks around the text shown (the last such text, the page on top).</summary>
    private static void Tap(Window window, string text)
    {
        Pump();
        var label = window.GetVisualDescendants().OfType<TextBlock>().Last(t => t.Text == text && t.IsEffectivelyVisible);
        var target = label.GetSelfAndVisualAncestors().OfType<Control>().First(c => c.IsSet(InputElement.CursorProperty));
        target.BringIntoView();
        window.UpdateLayout();
        Click(window, Centre(target, window));
    }
}
