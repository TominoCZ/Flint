using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Flint.App.Assets;
using Flint.App.Controls;
using Flint.App.Tone;
using Flint.Core.Connection;
using Flint.Core.Library;
using Flint.Core.Model;
using Flint.Core.Protocol;
using Flint.Core.Protocol.Payloads;
using Flint.Core.Tests;

namespace Flint.App.Tests;

// The whole window over FakeAmp with the owner's assets/: gestures on the Tone screen reach the amp as the official
// app's bytes, and the amp's own moves reach the screen.
[Collection(Headless.Name)]
public sealed class ToneScreenTests(Headless headless) : IAsyncDisposable
{
    private static readonly AssetStore? Assets = AssetStore.Find();
    private static readonly SupervisorTimings Fast = new(TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(30), 3, [TimeSpan.FromMilliseconds(20)]);

    private readonly FakeAmp _amp = new();
    private readonly string _library = Directory.CreateTempSubdirectory("flint-tone-").FullName;
    private FakeLink? _link;
    private AmpController? _controller;

    public async ValueTask DisposeAsync()
    {
        if (_controller is not null) await Task.Run(() => _controller.DisposeAsync().AsTask());
        Directory.Delete(_library, recursive: true);
    }

    [Fact]
    public async Task Until_the_first_sync_the_connecting_screen_shows_and_its_button_cancels()
    {
        if (Assets is null) return;
        _controller = new AmpController(() => new FakeLink { HangOnConnect = true }, new PresetLibrary(_library), Fast);
        _controller.Start();
        await headless.Run(async () =>
        {
            var window = new MainWindow(_controller, Assets, () => true) { Width = 1280, Height = 860 };
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.NotEmpty(window.GetVisualDescendants().OfType<Flint.App.Connect.ConnectScreen>());
            Assert.Empty(window.GetVisualDescendants().OfType<ChainStrip>());

            var cancel = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Cancel");
            var at = cancel.TranslatePoint(new Point(cancel.Bounds.Width / 2, cancel.Bounds.Height / 2), window)!.Value;
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
            await Wait.Until(() =>
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                return _controller.Link == LinkState.Idle && cancel.Content as string == "Connect";
            });
            window.Close();
        });
    }

    [Fact]
    public async Task Pedal_knobs_and_the_EQs_sliders_turn_the_parameter_the_apps_binding_gives_them()
    {
        if (Assets is null) return;
        _amp.Live = _amp.Slots[0];
        _controller = new AmpController(() => _link = new FakeLink { Respond = _amp.Respond }, new PresetLibrary(_library), Fast);
        _controller.Start();
        await Wait.Until(() => _controller.Link == LinkState.Ready && _controller.State is not null);

        await headless.Run(async () =>
        {
            SparkFonts.Register(Assets);
            var window = new MainWindow(_controller, Assets) { Width = 1280, Height = 860 };
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            // The Guitar EQ: slider_00 (tag binding_1) is param 0 in its binding. Drag its thumb up a little.
            Click(window, Centre(window.GetVisualDescendants().OfType<ChainTab>().Single(t => t.Title == "MOD/EQ"), window));
            var sliders = window.GetVisualDescendants().OfType<SliderControl>().ToList();
            Assert.Equal(7, sliders.Count);
            var first = sliders[0];
            var thumb = first.TranslatePoint(new Point(first.Bounds.Width / 2, first.Bounds.Height * 0.15 + (1 - first.Value) * first.Bounds.Height * 0.6868), window)!.Value;
            window.MouseDown(thumb, MouseButton.Left);
            window.MouseMove(thumb - new Point(0, 15));
            window.MouseUp(thumb - new Point(0, 15), MouseButton.Left);
            await Wait.Until(() => _link!.Written.Any(c => c.Op == Opcode.SetParameter && DspParam.Decode(c.Payload).Module == "GuitarEQ6"));
            Assert.Equal(0, DspParam.Decode(_link!.Written.Last(c => c.Op == Opcode.SetParameter).Payload).Param);

            // The echo tape: its first knob turns what the catalog (the app's binding) says.
            Click(window, Centre(window.GetVisualDescendants().OfType<ChainTab>().Single(t => t.Title == "DELAY"), window));
            var knob = window.GetVisualDescendants().OfType<PedalPanel>().Single().GetVisualDescendants().OfType<KnobControl>().First();
            var expected = Assets.Catalog.Panels!["editor_delay_echo_tape"].Values.Where(c => c.Kind == "knob").Select(c => c.Param).ToList();
            var at = Centre(knob, window);
            window.MouseDown(at, MouseButton.Left);
            window.MouseMove(at - new Point(0, 20));
            window.MouseUp(at - new Point(0, 20), MouseButton.Left);
            await Wait.Until(() => _link.Written.Any(c => c.Op == Opcode.SetParameter && DspParam.Decode(c.Payload).Module == "DelayRe201"));
            Assert.Contains(DspParam.Decode(_link.Written.Last(c => c.Op == Opcode.SetParameter).Payload).Param, expected);
            window.Close();
        });
    }

    [Fact]
    public async Task Picking_a_model_in_the_sheet_swaps_it_on_the_amp_and_in_the_panel()
    {
        if (Assets is null) return;
        _amp.Live = _amp.Slots[0];
        _controller = new AmpController(() => _link = new FakeLink { Respond = _amp.Respond }, new PresetLibrary(_library), Fast);
        _controller.Start();
        await Wait.Until(() => _controller.Link == LinkState.Ready && _controller.State is not null);

        await headless.Run(async () =>
        {
            SparkFonts.Register(Assets);
            var window = new MainWindow(_controller, Assets) { Width = 1280, Height = 860 };
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var amp = window.GetVisualDescendants().OfType<ChainTab>().Single(t => t.Title == "AMP");
            window.MouseDown(Centre(amp, window), MouseButton.Left);
            window.MouseUp(Centre(amp, window), MouseButton.Left);
            window.MouseDown(Centre(amp, window), MouseButton.Left);
            window.MouseUp(Centre(amp, window), MouseButton.Left);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            var row = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Sunny 3000").GetVisualAncestors().OfType<Border>().First(b => b.Cursor is not null);
            row.BringIntoView();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var at = Centre(row, window);
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
            // The pick shows at once, before the amp has answered.
            Assert.NotEmpty(row.GetVisualDescendants().OfType<ProgressBar>());
            await Wait.Until(() =>
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                return _controller.State!.Live.Modules.Any(m => m.DspId == "Sunny3000");
            });
            // Done: a fresh sheet, the new model current, nothing running.
            await Wait.Until(() =>
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                return !window.GetVisualDescendants().OfType<ProgressBar>().Any();
            });
            Assert.Equal(new DspReplace("GK800", "Sunny3000"), DspReplace.Decode(_link!.Written.Single(c => c.Op == Opcode.ReplaceModule).Payload));

            // Two quick picks, the second while the first is still under way: both reach the amp, in order, the second
            // swapping from the first's model (it was dropped once, when one action at a time was allowed).
            Tap(window, "W600");
            Tap(window, "Hammer 500");
            await Wait.Until(() =>
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                return _controller.State!.Live.Modules.Any(m => m.DspId == "Hammer500")
                       && !window.GetVisualDescendants().OfType<ProgressBar>().Any();
            });
            var swaps = _link.Written.Where(c => c.Op == Opcode.ReplaceModule).Select(c => DspReplace.Decode(c.Payload)).ToList();
            Assert.Equal([new DspReplace("Sunny3000", "W600"), new DspReplace("W600", "Hammer500")], swaps.Skip(1));
            window.Close();
        });
    }

    [Fact]
    public async Task Knobs_swipes_and_the_amps_own_knob_all_go_through()
    {
        if (Assets is null) return;
        _amp.Live = _amp.Slots[0];
        _controller = new AmpController(() => _link = new FakeLink { Respond = _amp.Respond }, new PresetLibrary(_library), Fast);
        _controller.Start();
        await Wait.Until(() => _controller.Link == LinkState.Ready && _controller.State is not null);

        await headless.Run(async () =>
        {
            SparkFonts.Register(Assets);
            var window = new MainWindow(_controller, Assets) { Width = 1280, Height = 860 };
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            // Drag the amp's first knob (GAIN, param 0) up by its height: a third of the range, sent as 01 04.
            var gain = window.GetVisualDescendants().OfType<KnobControl>().First();
            var before = gain.Value;
            var centre = gain.TranslatePoint(new Point(gain.Bounds.Width / 2, gain.Bounds.Height / 2), window)!.Value;
            var height = gain.TranslatePoint(new Point(0, gain.Bounds.Height), window)!.Value.Y - gain.TranslatePoint(default, window)!.Value.Y; // scaled on screen
            window.MouseDown(centre, MouseButton.Left);
            window.MouseMove(centre - new Point(0, height));
            window.MouseUp(centre - new Point(0, height), MouseButton.Left);
            await Wait.Until(() => _link!.Written.Any(c => c.Op == Opcode.SetParameter));
            var sent = DspParam.Decode(_link!.Written.Last(c => c.Op == Opcode.SetParameter).Payload);
            Assert.Equal(("GK800", 0), (sent.Module, sent.Param));
            Assert.Equal(Math.Min(1, before + 1 / 3.0), sent.Value, 2);

            // Swipe DELAY (off in this preset) down: switched on with 01 15.
            var delay = window.GetVisualDescendants().OfType<ChainTab>().Single(t => t.Title == "DELAY");
            var at = delay.TranslatePoint(new Point(delay.Bounds.Width / 2, 60), window)!.Value;
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at + new Point(0, 40), MouseButton.Left);
            await Wait.Until(() => _link.Written.Any(c => c.Op == Opcode.SetModuleOn));
            Assert.Equal(new DspOnOff("DelayRe201", true), DspOnOff.Decode(_link.Written.Last(c => c.Op == Opcode.SetModuleOn).Payload));

            // DELAY's own panel: its footswitch switches it back off, and its LED follows.
            Click(window, Centre(delay, window));
            var pedal = window.GetVisualDescendants().OfType<PedalPanel>().Single();
            var footswitch = pedal.GetVisualDescendants().OfType<Border>().Single(b => b.Cursor is not null && b.Background == Avalonia.Media.Brushes.Transparent);
            var switches = _link.Written.Count(c => c.Op == Opcode.SetModuleOn);
            Click(window, Centre(footswitch, window));
            await Wait.Until(() => _link.Written.Count(c => c.Op == Opcode.SetModuleOn) > switches);
            Assert.Equal(new DspOnOff("DelayRe201", false), DspOnOff.Decode(_link.Written.Last(c => c.Op == Opcode.SetModuleOn).Payload));
            await Wait.Until(() =>
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                return _controller.State!.Live.Modules.Single(m => m.DspId == "DelayRe201").Active == false;
            });
            Click(window, Centre(window.GetVisualDescendants().OfType<ChainTab>().Single(t => t.Title == "AMP"), window));

            // The amp's own VOLUME knob (param 4): the fifth knob on the panel follows.
            _link.Send(new Chunk(0x40, 0x03, 0x37, new DspParam("GK800", 4, 0.25f).Encode()));
            await Wait.Until(() =>
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                return Math.Abs(window.GetVisualDescendants().OfType<KnobControl>().ElementAt(4).Value - 0.25) < 0.001;
            });
            window.Close();
        });
    }

    [Fact]
    public async Task Save_to_hardware_from_the_menu_names_the_preset_and_uploads_it_to_the_picked_slot()
    {
        if (Assets is null) return;
        _amp.Live = _amp.Slots[0];
        _controller = new AmpController(() => _link = new FakeLink { Respond = _amp.Respond }, new PresetLibrary(_library), Fast);
        _controller.Start();
        await Wait.Until(() => _controller.Link == LinkState.Ready && _controller.State is not null);

        await headless.Run(async () =>
        {
            SparkFonts.Register(Assets);
            var window = new MainWindow(_controller, Assets) { Width = 1280, Height = 860 };
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            window.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "tone-main.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            var tone = window.GetVisualDescendants().OfType<ToneScreen>().Single();
            // The ⋮ button: the 44 dp button with a hand of its own (the title's images inherit the title's).
            Click(window, Centre(tone.GetVisualDescendants().OfType<Border>().Single(b => b.IsSet(InputElement.CursorProperty) && b.Width == 44), window));
            window.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "tone-actions.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            Click(window, Centre(Text(window, "Save to hardware preset"), window));
            var dialog = window.GetVisualDescendants().OfType<PresetNameDialog>().Single();
            Assert.Equal("Preset 0", dialog.NameField.Text);
            dialog.NameField.Text = "Lead";
            Click(window, Centre(window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "CH1A"), window));
            var ch2b = window.GetVisualDescendants().OfType<RadioButton>().Single(r => r.Content as string == "CH2B");
            ch2b.BringIntoView(); // the picker shows four slots at a time
            window.UpdateLayout();
            Click(window, Centre(ch2b, window));
            Click(window, Centre(window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "OK"), window));
            Click(window, Centre(window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "SAVE"), window));

            await Wait.Until(() => _amp.Selected.Contains(5));
            Assert.Equal("Lead", _amp.Slots[5].Name);
            Assert.Equal([5], _amp.Uploaded);
            Assert.Empty(window.GetVisualDescendants().OfType<PresetNameDialog>());
            await Wait.Until(() =>
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                return window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Lead saved to Spark 2 CH2B" && t.IsEffectivelyVisible);
            });
            window.Close();
        });
    }

    [Fact]
    public async Task The_preset_list_selects_a_slot_from_its_bank_and_plays_a_library_preset_from_All()
    {
        if (Assets is null) return;
        _amp.Live = _amp.Slots[0];
        _controller = new AmpController(() => _link = new FakeLink { Respond = _amp.Respond }, new PresetLibrary(_library), Fast);
        _controller.Start();
        await Wait.Until(() => _controller.Link == LinkState.Ready && _controller.State is not null);
        _controller.SaveAsNew("Practice");

        await headless.Run(async () =>
        {
            SparkFonts.Register(Assets);
            var window = new MainWindow(_controller, Assets) { Width = 1280, Height = 860 };
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            var title = Centre(Text(window, "Practice"), window);
            Click(window, title);
            Assert.Single(window.GetVisualDescendants().OfType<PresetList>());
            Click(window, new Point(1200, 700)); // beside the dropdown: it closes
            Assert.Empty(window.GetVisualDescendants().OfType<PresetList>());
            Click(window, title);
            Click(window, Centre(Text(window, "HW Bank B"), window));
            window.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "tone-presets.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            Click(window, Centre(Text(window, "Preset 5"), window));
            await Wait.Until(() => _amp.Selected.Contains(5));
            await Wait.Until(() =>
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                return _controller.State!.CurrentSlot == 5 && !window.GetVisualDescendants().OfType<PresetList>().Any();
            });

            Click(window, Centre(window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Preset 5"), window)); // the name in the top bar
            Click(window, Centre(Text(window, "All"), window));
            Click(window, Centre(Text(window, "Practice"), window));
            await Wait.Until(() => _amp.Selected.Contains(127));
            Assert.Equal("Practice", _amp.Live!.Name);
            window.Close();
        });
    }

    private static Control Text(Window window, string text) =>
        window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == text);

    private static Point Centre(Control c, Window window) => c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), window)!.Value;

    private static void Click(Window window, Point at)
    {
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Thread.Sleep(350); // past the strip's double-tap window
    }

    /// <summary>Taps the model sheet's row of that name, without waiting for the amp.</summary>
    private static void Tap(Window window, string model)
    {
        var row = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == model).GetVisualAncestors().OfType<Border>().First(b => b.Cursor is not null);
        row.BringIntoView();
        window.UpdateLayout(); // scrolled before the click, or it can land on whatever was there
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var at = Centre(row, window);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
    }
}
