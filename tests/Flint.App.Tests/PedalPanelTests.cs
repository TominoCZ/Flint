using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Flint.App.Assets;
using Flint.App.Controls;
using Flint.App.Tone;
using Flint.Core.Layout;

namespace Flint.App.Tests;

// The Auto Wah's panel is wired from its fragment's code, not a data binding: its one knob is param 4.
[Collection(Headless.Name)]
public sealed class PedalPanelTests(Headless headless)
{
    private static readonly AssetStore? Assets = AssetStore.Find();

    [Fact]
    public async Task Every_pedal_panel_in_the_catalog_lays_out()
    {
        if (Assets is null) return;
        await headless.Session.Dispatch(() =>
        {
            var values = new AndroidValues(Path.Combine(Assets.Root, "res"));
            foreach (var layout in Assets.Catalog.Chain.SelectMany(s => s.Models).Select(m => m.Editor[0]).Where(e => !e.StartsWith("editor_amp")).Distinct())
            {
                var window = new Window { Width = 360, Height = 444, Content = new PedalPanel(Assets, values, layout, null, new LayoutContext(_ => null, values.Dimen)) };
                window.Show();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task The_Auto_Wahs_sensitivity_knob_turns_param_4()
    {
        if (Assets is null) return;
        await headless.Session.Dispatch(() =>
        {
            var values = new AndroidValues(Path.Combine(Assets.Root, "res"));
            var panel = new PedalPanel(Assets, values, "editor_auto_wah_phone", null, new LayoutContext(_ => null, values.Dimen));
            var window = new Window { Width = 360, Height = 444, Content = panel };
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var turned = new List<int>();
            panel.Turned += (param, _) => turned.Add(param);

            var knob = window.GetVisualDescendants().OfType<KnobControl>().Single(k => k.IsHitTestVisible);
            var at = knob.TranslatePoint(new Point(knob.Bounds.Width / 2, knob.Bounds.Height / 2), window)!.Value;
            window.MouseDown(at, MouseButton.Left);
            window.MouseMove(at - new Point(0, 20));
            window.MouseUp(at - new Point(0, 20), MouseButton.Left);

            Assert.Equal(4, Assert.Single(turned.Distinct()));
            window.Close();
        }, CancellationToken.None);
    }
}
