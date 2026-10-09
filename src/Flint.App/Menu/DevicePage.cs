using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Flint.Core.Connection;
using Flint.App.Controls;
using Flint.Core.Model;

namespace Flint.App.Menu;

/// <summary>
/// The amp's device page as the app shows a Spark 2 (<c>SparkDeviceDetailFragment</c> with config <c>L5.O</c>): Tone Control, Power Settings, Line Out Mute Behavior (firmware 2.4 and later), Name, Model and Firmware Version.
/// Left out: Bluetooth Audio (what the amp's A2DP field means is not known), Battery (the owner's amp reports none), the
/// serial number (never read), firmware updates, factory reset and Forget.
/// </summary>
internal static class DevicePage
{
    private static readonly Version LineOutMuteSince = new(2, 4, 0, 0); // JamUpDevice's gate for Spark 2

    public static TabPage Create(PageStack menu) => new(
        s => (s.Name, menu.Amp.Link == LinkState.Ready, s.LineOutMuted, s.Firmware),
        s => Build(menu, s));

    private static Control Build(PageStack menu, AmpState state)
    {
        var (rows, text) = (menu.Rows, menu.Rows.Values);
        var connected = menu.Amp.Link == LinkState.Ready;
        var picture = rows.Picture("ic_menu_pager_device_2");
        (picture.Width, picture.Height, picture.Margin, picture.HorizontalAlignment) = (160, 128, new Thickness(0, 0, 0, 24), HorizontalAlignment.Center);

        var list = new List<Control>
        {
            rows.TopBar(null, menu.Pop, height: 52, backMargin: 10),
            picture,
            rows.Status(text.Text("profile_devicedetail_ampconnectstatus_tonecontrol"),
                text.Text(connected ? "profile_devicedetail_ampconnectstatus_tonecontrol_connected" : "profile_devicedetail_ampconnectstatus_tonecontrol_disconnected"),
                connected),
            rows.Divider(),
            rows.Info(text.Text("spark10_powersettings_title"), null, open: () => menu.Push(PowerSettingsPage.Create(menu))),
        };
        if (Version.TryParse(state.Firmware, out var firmware) && firmware >= LineOutMuteSince)
            list.Add(rows.Switch(text.Text("devicecard_lineoutmute_title"), text.Text("devicecard_lineoutmute_desc"), state.LineOutMuted,
                () => menu.Run("set the line-out mute", () => menu.Amp.SetLineOutMutedAsync(!state.LineOutMuted))));
        list.Add(rows.Divider());
        list.Add(rows.Info(text.Text("profile_devicedetail_name"), state.Name, open: () => menu.Push(AmpNameEditor.Create(menu, state.Name))));
        list.Add(rows.Info(text.Text("profile_devicedetail_model"), "Spark 2"));
        list.Add(rows.Info(text.Text("profile_devicedetail_firmwareversion"), null, second: state.Firmware));
        return PageStack.Scrolling([.. list]);
    }
}
