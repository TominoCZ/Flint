using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Flint.App.Assets;
using Flint.App.Tuner;
using Flint.Core.Connection;
using Flint.Core.Library;
using Flint.Core.Model;
using Flint.Core.Protocol;
using Flint.Core.Protocol.Payloads;
using Flint.Core.Tests;
using Xunit.Abstractions;

namespace Flint.App.Tests;

// The tuner over FakeAmp with the owner's assets/: the amp's tuner button opens it and the window answers with 01 65 as
// the app does; readings move it; the X switches the amp's tuner off. Frames are saved as tuner-*.png.
[Collection(Headless.Name)]
public sealed class TunerScreenTests(Headless headless, ITestOutputHelper output) : IAsyncDisposable
{
    private static readonly AssetStore? Assets = AssetStore.Find();

    private readonly FakeAmp _amp = new();
    private readonly string _library = Directory.CreateTempSubdirectory("flint-tuner-").FullName;
    private FakeLink? _link;
    private AmpController? _controller;
    private byte _seq = 0x40;

    public async ValueTask DisposeAsync()
    {
        if (_controller is not null) await Task.Run(() => _controller.DisposeAsync().AsTask());
        Directory.Delete(_library, recursive: true);
    }

    [Fact]
    public async Task The_amps_tuner_button_opens_it_readings_show_and_the_X_switches_it_off()
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

            Notify(0x65, new BoolValue(true));
            await Until(() => _amp.TunerOn); // answered with our own 01 65
            Assert.Single(window.GetVisualDescendants().OfType<TunerScreen>());
            await Task.Delay(1100); // the ticks open over a second
            Save(window, "tuner-waiting");

            Notify(0x64, new TunerReading(9, 0.3f));
            await Until(() => _controller.State!.Tuning?.Note == 9);
            Save(window, "tuner-flat");
            (window.Width, window.Height) = (1568, 900); // a wide desktop window: the arcs keep the phone's curve
            Save(window, "tuner-flat-wide");
            (window.Width, window.Height) = (540, 960);
            Notify(0x64, new TunerReading(4, 0.5f));
            await Until(() => _controller.State!.Tuning?.Note == 4);
            Save(window, "tuner-in-tune");

            // The same note a little sharp: the dial glides there over a few frames instead of jumping.
            var dial = window.GetVisualDescendants().OfType<TunerDial>().Single();
            Notify(0x64, new TunerReading(4, 0.6f));
            await Until(() => ReferenceEquals(dial.Reading, _controller.State!.Tuning) && _controller.State.Tuning!.Offset > 0.55f);
            Assert.InRange(dial.ShownOffset, 0.49, 0.59);
            await Until(() => Math.Abs(dial.ShownOffset - 0.6) < 0.001);

            var close = window.GetVisualDescendants().OfType<TunerScreen>().Single().GetVisualDescendants().OfType<Image>().Single(i => i.Cursor is not null);
            var at = close.TranslatePoint(new Point(12, 12), window)!.Value;
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
            await Until(() => !_amp.TunerOn && !window.GetVisualDescendants().OfType<TunerScreen>().Any());
            window.Close();
        });
    }

    private void Notify(byte sub, IPayload payload) => _link!.Send(new Chunk(_seq++, 0x03, sub, payload.Encode()));

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
}
