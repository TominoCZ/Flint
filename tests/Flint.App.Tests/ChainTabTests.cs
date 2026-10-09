using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Flint.App.Tone;

namespace Flint.App.Tests;

// The strip's gestures, as on the phone: tap selects, double-tap opens the model sheet, a vertical
// swipe switches the device, down for on and up for off.
[Collection(Headless.Name)]
public sealed class ChainTabTests(Headless headless)
{
    [Fact]
    public async Task Tap_double_tap_and_swipes_each_do_their_one_thing()
    {
        await headless.Session.Dispatch(() =>
        {
            var tab = new ChainTab("Delay", 5);
            var events = new List<string>();
            tab.Chosen += t => events.Add($"chosen {t.Slot}");
            tab.ModelsRequested += t => events.Add($"models {t.Slot}");
            tab.Swiped += (t, on) => events.Add($"swiped {t.Slot} {(on ? "on" : "off")}");
            var window = new Window { Width = 100, Height = ChainTab.StripHeight, Content = tab };
            window.Show();
            var middle = new Point(50, 80);

            window.MouseDown(middle, MouseButton.Left);
            window.MouseUp(middle, MouseButton.Left);
            window.MouseDown(middle, MouseButton.Left);
            window.MouseUp(middle, MouseButton.Left);
            Thread.Sleep(350); // past the double-tap window
            window.MouseDown(middle, MouseButton.Left);
            window.MouseMove(middle + new Point(0, 40));
            window.MouseUp(middle + new Point(0, 40), MouseButton.Left);
            window.MouseDown(middle, MouseButton.Left);
            window.MouseUp(middle - new Point(0, 40), MouseButton.Left);
            window.MouseDown(middle, MouseButton.Left);
            window.MouseUp(middle + new Point(30, 10), MouseButton.Left); // mostly sideways: a tap, not a swipe

            Assert.Equal(["chosen 5", "models 5", "swiped 5 on", "swiped 5 off", "chosen 5"], events);
            window.Close();
        }, CancellationToken.None);
    }
}
