using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Flint.App.Assets;
using Flint.App.Controls;

namespace Flint.App.Tone;

/// <summary>What the ⋮ menu offers, in the app's order (<c>ToneActionsAdapter</c>).</summary>
internal enum ToneAction
{
    Overwrite,
    SaveAsNew,
    ShareToToneCloud,
    SaveToHardware,
    Export,
    Import,
}

/// <summary>
/// The ⋮ menu's rows (<c>popup_tone_actions.xml</c>): the app's white rows, shown as a dropdown under the ⋮ button
/// rather than the phone's sheet across the foot of the screen. Save to hardware preset shows only
/// while connected, as on the phone. ToneCloud is out of scope, so its row is dimmed and does nothing. The
/// app's Delete, for presets not in a slot, is not here yet. Export and Import are ours: the phone
/// shares presets through ToneCloud instead.
/// </summary>
internal sealed class ActionsMenu : StackPanel
{
    private static readonly IBrush Text = new SolidColorBrush(Color.Parse("#de000000"));

    public ActionsMenu(AndroidValues values, bool connected)
    {
        Width = 240;
        Background = Brushes.White;
        Add(values, "tone_action_overwrite", ToneAction.Overwrite);
        Add(values, "tone_action_saveasnew", ToneAction.SaveAsNew);
        Add(values, "tone_action_tonecloud", null);
        if (connected) Add(values, "tone_action_uptodevice", ToneAction.SaveToHardware);
        Add(values, "Export preset…", ToneAction.Export);
        Add(values, "Import presets…", ToneAction.Import);
    }

    /// <summary>A row was tapped; the menu has closed.</summary>
    public event Action<ToneAction>? Chosen;

    public event Action? Closed;

    private void Add(AndroidValues values, string text, ToneAction? action)
    {
        var row = new Border
        {
            Height = 48, Background = Brushes.White, Opacity = action is null ? 0.35 : 1,
            Child = new TextBlock
            {
                Text = values.Text(text), FontSize = 16, FontFamily = SparkFonts.Roboto, Foreground = Text,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0),
            },
        };
        if (action is { } chosen)
        {
            Clicks.On(row, () =>
            {
                Closed?.Invoke();
                Chosen?.Invoke(chosen);
            });
        }
        Children.Add(row);
    }
}
