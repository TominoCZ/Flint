using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Flint.App.Assets;
using Flint.App.Controls;
using Flint.App.Tone;
using Flint.Core.Layout;
using Xunit.Abstractions;

namespace Flint.App.Tests;

// The RB-800 panel from the owner's own assets/, against what uiautomator reported on the owner's phone
// (research/ui/phone/walk1/04-AMP.xml, 1080 px wide at 3 px per dp, the editor starting at y = 889).
[Collection(Headless.Name)]
public sealed class AmpPanelTests(Headless headless, ITestOutputHelper output)
{
    private static readonly AssetStore? Assets = AssetStore.Find();

    [Fact]
    public async Task The_panel_lands_where_the_phone_draws_it()
    {
        if (Skip()) return;
        await headless.Session.Dispatch(() =>
        {
            var (window, panel) = Show();
            var knobs = panel.GetVisualDescendants().OfType<KnobControl>().ToList();
            (int L, int T, int R, int B)[] phone = [(202, 1229, 304, 1331), (353, 1236, 437, 1320), (485, 1229, 587, 1331), (636, 1236, 721, 1320), (770, 1229, 873, 1331)];
            Assert.Equal(5, knobs.Count);
            for (var i = 0; i < 5; i++)
            {
                var at = knobs[i].Bounds;
                output.WriteLine($"knob {i}: {at}  phone {phone[i].L / 3.0:F2},{(phone[i].T - 889) / 3.0:F2} {(phone[i].R - phone[i].L) / 3.0:F2}  window {window.ClientSize}");
                Assert.InRange(Math.Abs(at.X - phone[i].L / 3.0), 0, 0.5);
                Assert.InRange(Math.Abs(at.Y - (phone[i].T - 889) / 3.0), 0, 0.5);
                Assert.InRange(Math.Abs(at.Width - (phone[i].R - phone[i].L) / 3.0), 0, 0.5);
            }
            var file = Path.Combine(AppContext.BaseDirectory, "amp-panel.png");
            window.CaptureRenderedFrame()!.Save(file, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            output.WriteLine($"rendered to {file}");
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Dragging_up_by_the_knobs_height_turns_it_a_third_and_names_its_param()
    {
        if (Skip()) return;
        await headless.Session.Dispatch(() =>
        {
            var (window, panel) = Show();
            var turns = new List<(int Param, float Value)>();
            panel.Turned += (param, value) => turns.Add((param, value));
            var bass = panel.GetVisualDescendants().OfType<KnobControl>().ElementAt(1); // the second knob is BASS, param 3
            bass.Value = 0.2;
            var centre = bass.TranslatePoint(new Point(bass.Bounds.Width / 2, bass.Bounds.Height / 2), window)!.Value;

            window.MouseDown(centre, MouseButton.Left);
            window.MouseMove(centre - new Point(0, bass.Bounds.Height));
            window.MouseUp(centre - new Point(0, bass.Bounds.Height), MouseButton.Left);

            Assert.Equal(0.2 + 1 / 3.0, bass.Value, 3);
            Assert.Equal(3, turns[^1].Param);
            Assert.False(bass.IsHeld);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Pressing_the_switch_on_the_face_toggles_the_amp()
    {
        if (Skip()) return;
        await headless.Session.Dispatch(() =>
        {
            var (window, panel) = Show();
            var toggled = 0;
            panel.PowerToggled += () => toggled++;
            // switch_power on the phone: [86,1269][165,1348]
            window.MouseDown(new Point(125 / 3.0, (1308 - 889) / 3.0), MouseButton.Left);
            Assert.Equal(1, toggled);
            window.Close();
        }, CancellationToken.None);
    }

    private static (Window, AmpPanel) Show()
    {
        var values = new AndroidValues(Path.Combine(Assets!.Root, "res"));
        var context = new LayoutContext(_ => null, values.Dimen);
        var panel = new AmpPanel(Assets, values, Assets.Catalog.Model("GK800")!, context);
        var window = new Window { Width = 1080 / 3.0, Height = 1331 / 3.0, Content = panel, Background = Avalonia.Media.Brushes.Black };
        window.Show();
        window.SetRenderScaling(3); // the phone's 3 px per dp, so layout rounding matches it
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, panel);
    }

    private bool Skip()
    {
        if (Assets is not null) return false;
        output.WriteLine("assets/ missing: run scripts/extract-assets.sh to check the panel against the phone");
        return true;
    }
}
