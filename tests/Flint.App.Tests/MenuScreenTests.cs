using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Flint.App.Assets;
using Flint.Core.Connection;
using Flint.Core.Library;
using Flint.Core.Model;
using Flint.Core.Protocol.Payloads;
using Flint.Core.Tests;
using Xunit.Abstractions;

namespace Flint.App.Tests;

// The Menu tab over FakeAmp with the owner's assets/: the device page's settings reach the amp. Frames are saved beside
// the test binary (menu-*.png) for a look.
[Collection(Headless.Name)]
public sealed class MenuScreenTests(Headless headless, ITestOutputHelper output) : IAsyncDisposable
{
    private static readonly AssetStore? Assets = AssetStore.Find();

    private readonly FakeAmp _amp = new();
    private readonly string _library = Directory.CreateTempSubdirectory("flint-menu-").FullName;
    private AmpController? _controller;

    public async ValueTask DisposeAsync()
    {
        if (_controller is not null) await Task.Run(() => _controller.DisposeAsync().AsTask());
        Directory.Delete(_library, recursive: true);
    }

    [Fact]
    public async Task The_device_page_sets_power_settings_line_out_mute_and_the_name_on_the_amp()
    {
        if (Assets is null) return;
        _controller = new AmpController(() => new FakeLink { Respond = _amp.Respond }, new PresetLibrary(_library));
        _controller.Start();
        await Wait.Until(() => _controller.Link == LinkState.Ready && _controller.State is not null);

        await headless.Run(async () =>
        {
            SparkFonts.Register(Assets);
            var window = new MainWindow(_controller, Assets) { Width = 540, Height = 960 };
            window.Show();
            Pump();

            Click(window, window.GetVisualDescendants().OfType<Controls.BottomNav>().Single(), 0.9); // the Menu tab
            Save(window, "menu-tab");
            Tap(window, "Spark 2"); // the amp's card
            Save(window, "menu-device");

            Tap(window, "Line Out Mute Behavior");
            await Wait.Until(() => _amp.LineOutMuted);

            Tap(window, "Power Settings");
            Tap(window, "Auto Standby");
            Save(window, "menu-standby");
            Tap(window, "30 Minutes");
            await Wait.Until(() => _amp.Settings.AutoStandby == 30);
            Assert.Equal(new SettingInfo(false, 60, 0, 30), _amp.Settings);

            var menu = window.GetVisualDescendants().OfType<Menu.MenuScreen>().Single();
            menu.Pop();
            menu.Pop(); // back to the device page
            Tap(window, "Name");
            window.GetVisualDescendants().OfType<TextBox>().Single().Text = "  Den ";
            Save(window, "menu-name");
            Tap(window, "Done");
            await Wait.Until(() => _amp.Name == "Den");
            Pump();
            Assert.NotNull(window.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Text == "Den")); // the device page shows it

            window.Close();
        });
    }

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

    /// <summary>Taps the control that takes taps around the text shown.</summary>
    private static void Tap(Window window, string text, bool last = false)
    {
        Pump();
        var matches = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Text == text).ToList();
        Assert.NotEmpty(matches);
        var label = last ? matches[^1] : matches[0];
        var target = label.GetSelfAndVisualAncestors().OfType<Control>().First(c => c.Cursor is not null);
        target.BringIntoView();
        Pump();
        var at = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value;
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Pump();
    }

    /// <summary>Clicks a control at a fraction of its width, halfway down.</summary>
    private static void Click(Window window, Control control, double across)
    {
        var at = control.TranslatePoint(new Point(control.Bounds.Width * across, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Pump();
    }
}
