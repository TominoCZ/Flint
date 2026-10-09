using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Flint.App.Setup;
using Xunit.Abstractions;

namespace Flint.App.Tests;

// The setup screen in the states a user meets, drawn from made-up steps: each says what is happening or what to do,
// and the buttons fit. Frames are saved beside the test binary (setup-*.png) for a look.
[Collection(Headless.Name)]
public sealed class SetupScreenTests(Headless headless, ITestOutputHelper output)
{
    private static readonly SetupStep JavaDone = new("Java", StepState.Done, "Java 21"), AdbDone = new("Android tools (adb)", StepState.Done, "/usr/bin/adb");

    [Fact]
    public Task Each_state_shows_what_is_happening_and_what_to_do() => headless.Run(async () =>
    {
        var (window, screen) = Open(update: false);

        screen.Show(Phone(new SetupStep("Java", StepState.NeedsYou, "Unpacking the Spark app needs Java, and this computer has none.",
            Command: "sudo apt install openjdk-17-jre-headless")), finished: false);
        Save(window, "setup-java");
        Assert.Contains("sudo apt install openjdk-17-jre-headless", Texts(window));
        Assert.Contains("Copy", Texts(window));
        Assert.DoesNotContain("Try again", Texts(window));
        Assert.Contains("No Android phone? Use an .apkm file instead…", Texts(window));

        screen.Show(Phone(JavaDone, AdbDone, new SetupStep("Your phone", StepState.NeedsYou,
            "Your phone is asking whether to allow USB debugging from this computer. Unlock it and tap Allow.")), finished: false);
        Save(window, "setup-allow");

        screen.Show(Phone(JavaDone, AdbDone, new("Your phone", StepState.Done, "Pixel 7 is connected"), new("The Spark app on your phone", StepState.Done, "Spark 4.6.2.10098"),
            new("Copying the Spark app", StepState.Done, "5 files, 112 MB"), new("Unpacking the Spark app", StepState.Running, "Unpacking: 6,036 of 17,803 parts", 0.34)), finished: false);
        Save(window, "setup-unpacking");
        Assert.Equal(0.34, window.GetVisualDescendants().OfType<ProgressBar>().Single().Value);

        screen.Show(Phone(JavaDone, AdbDone, new("Your phone", StepState.Done, "Pixel 7 is connected"), new("The Spark app on your phone", StepState.Done, "Spark 4.6.2.10098"),
            new("Copying the Spark app", StepState.Failed, "could not copy base.apk from the phone: device offline")), finished: false);
        Save(window, "setup-failed");
        Assert.Contains("Try again", Texts(window));
        Assert.DoesNotContain("Cancel", Texts(window)); // a first run has nothing to go back to

        screen.Show(Phone(JavaDone, AdbDone, new("Your phone", StepState.Done, "Pixel 7 is connected"), new("The Spark app on your phone", StepState.Done, "Spark 4.6.2.10098"),
            new("Copying the Spark app", StepState.Done, "5 files, 112 MB"), new("Unpacking the Spark app", StepState.Done, "Unpacked"),
            new("Taking the artwork", StepState.Done, "Done")), finished: true);
        Save(window, "setup-done");
        Assert.Contains("Start Flint with it", Texts(window));
        Assert.DoesNotContain("No Android phone? Use an .apkm file instead…", Texts(window));
        Assert.Empty(window.GetVisualDescendants().OfType<ProgressBar>());
        await Task.CompletedTask;
    });

    [Fact]
    public Task An_update_can_be_cancelled_and_says_so() => headless.Run(async () =>
    {
        var (window, screen) = Open(update: true);
        var closed = false;
        screen.Closed += () => closed = true;
        screen.Show(Phone(JavaDone, AdbDone, new SetupStep("Your phone", StepState.Running, "Looking for your phone")), finished: false);
        Save(window, "setup-update");
        Assert.Contains("Update the Spark artwork", Texts(window));
        Assert.Single(window.GetVisualDescendants().OfType<ProgressBar>(), p => p.IsIndeterminate); // running, no measure yet

        var cancel = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Cancel");
        cancel.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.True(closed);
        await Task.CompletedTask;
    });

    /// <summary>The phone's seven steps: the ones given, then the rest still waiting.</summary>
    private static List<SetupStep> Phone(params SetupStep[] steps)
    {
        string[] titles = ["Java", "Android tools (adb)", "Your phone", "The Spark app on your phone", "Copying the Spark app", "Unpacking the Spark app", "Taking the artwork"];
        return [.. steps, .. titles.Skip(steps.Length).Select(t => new SetupStep(t))];
    }

    private static (Window, SetupScreen) Open(bool update)
    {
        var screen = new SetupScreen(Path.Combine(Path.GetTempPath(), "flint-setup-never-run"), update);
        var window = new Window { Width = 900, Height = 900, Content = screen };
        window.Show();
        return (window, screen);
    }

    private static List<string> Texts(Window window) =>
        [.. window.GetVisualDescendants().Where(v => v.IsEffectivelyVisible).Select(v => v switch { TextBlock t => t.Text, TextBox b => b.Text, _ => null }).OfType<string>()];

    private void Save(Window window, string name)
    {
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var file = Path.Combine(AppContext.BaseDirectory, name + ".png");
        window.CaptureRenderedFrame()!.Save(file, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        output.WriteLine($"rendered to {file}");
    }
}
