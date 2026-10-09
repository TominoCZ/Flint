using Avalonia;
using Avalonia.Controls;
using Flint.App.Assets;
using Flint.App.Controls;
using Flint.Core.Model;
using Flint.Core.Protocol.Payloads;

namespace Flint.App.Menu;

/// <summary>
/// Power Settings (<c>DevicePowerSettingFragment</c>) and its two pickers (<c>DeviceAutoStandByFragment</c>,
/// <c>DeviceAutoShutDownFragment</c>), with a Spark 2's choices. A pick goes to the amp at once with the other settings
/// as they are (<c>01 72</c>). Standby and shutdown cannot both be 30 minutes: that choice is greyed on each.
/// </summary>
internal static class PowerSettingsPage
{
    private static readonly (uint Minutes, string Label)[] Standby =
    [
        (5, "spark10_autostandby_5min_btn"), (10, "spark10_autostandby_10min_btn"), (15, "spark10_autostandby_15min_btn"),
        (30, "spark10_autostandby_30min_btn"), (0, "spark10_autostandby_never_btn"),
    ];

    // Never is offered only on the Spark pedal.
    private static readonly (uint Minutes, string Label)[] Shutdown =
    [
        (30, "spark10_autoshutdown_30min_btn"), (40, "spark10_autoshutdown_40min_btn"), (50, "spark10_autoshutdown_50min_btn"),
        (60, "spark10_autoshutdown_60min_btn"),
    ];

    public static TabPage Create(PageStack menu) => new(s => s.Settings, s =>
    {
        var text = menu.Rows.Values;
        var rows = new Control[]
        {
            menu.Rows.TopBar(text.Text("spark10_powersettings_title"), menu.Pop),
            menu.Rows.Info(text.Text("spark10_autostandby_btn"), Label(Standby, s.Settings.AutoStandby, text), open: () => menu.Push(StandbyPicker(menu))),
            menu.Rows.Info(text.Text("spark10_autoshutdown_btn"), Label(Shutdown, s.Settings.AutoShutdown, text), open: () => menu.Push(ShutdownPicker(menu))),
        };
        rows[1].Margin = new Thickness(0, 12, 0, 0);
        return PageStack.Scrolling(rows);
    });

    private static TabPage StandbyPicker(PageStack menu) => Picker(menu, "spark10_autostandby_title", "spark10_autostandby_desc", Standby,
        s => s.AutoStandby, m => m != 30 || menu.Amp.State?.Settings.AutoShutdown != 30, (s, m) => s with { AutoStandby = m });

    private static TabPage ShutdownPicker(PageStack menu) => Picker(menu, "spark10_autoshutdown_title", "spark10_autoshutdown_desc", Shutdown,
        s => s.AutoShutdown, m => m != 30 || menu.Amp.State?.Settings.AutoStandby != 30, (s, m) => s with { AutoShutdown = m });

    private static TabPage Picker(PageStack menu, string title, string hint, (uint Minutes, string Label)[] choices,
        Func<SettingInfo, uint> current, Func<uint, bool> enabled, Func<SettingInfo, uint, SettingInfo> with) => new(s => s.Settings, s =>
    {
        var text = menu.Rows.Values;
        var rows = new List<Control> { menu.Rows.TopBar(text.Text(title), menu.Pop) };
        foreach (var (minutes, label) in choices)
            rows.Add(menu.Rows.Choice(text.Text(label), current(s.Settings) == minutes, enabled(minutes),
                () => menu.Run($"set {text.Text(title)}", () => menu.Amp.SetSettingsAsync(with(menu.Amp.State!.Settings, minutes)))));
        rows[1].Margin = new Thickness(rows[1].Margin.Left, rows[1].Margin.Top + 12, rows[1].Margin.Right, rows[1].Margin.Bottom);
        rows.Add(menu.Rows.Divider());
        rows.Add(menu.Rows.Hint(text.Text(hint)));
        return PageStack.Scrolling([.. rows]);
    });

    /// <summary>A setting's choice as the app names it, or nothing for a value the app does not offer.</summary>
    private static string Label((uint Minutes, string Label)[] choices, uint minutes, AndroidValues text) =>
        choices.FirstOrDefault(c => c.Minutes == minutes).Label is { } label ? text.Text(label) : "";
}
