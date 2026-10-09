using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Flint.App.Assets;
using Flint.App.Controls;
using Flint.Core.Model;

namespace Flint.App.Menu;

/// <summary>
/// The amp's name editor (<c>NameEditFragment</c>, <c>fragment_device_name_edit.xml</c>): Cancel and Done at the top, the
/// name in the middle with a character count under it, up to 40 characters. Done is greyed while the name is empty.
/// </summary>
internal static class AmpNameEditor
{
    public static TabPage Create(PageStack menu, string name) => new(_ => "editing", _ => Build(menu, name));

    private static Control Build(PageStack menu, string name)
    {
        var (rows, text) = (menu.Rows, menu.Rows.Values);
        var white30 = rows.Colour("white30");
        var cancel = rows.Text(text.Text("profile_devicedetail_rename_cancel_btn"), 14, rows.Colour("white70pa"), semiBold: true);
        var done = rows.Text(text.Text("profile_devicedetail_rename_done_btn"), 14, rows.Colour("spark_red_500"), semiBold: true, HorizontalAlignment.Right);
        cancel.Margin = done.Margin = new Thickness(20);
        var field = new TextBox
        {
            Text = name, MaxLength = AmpController.MaxAmpName, FontSize = 18, FontFamily = SparkFonts.Raleway, FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.White, Background = Brushes.Transparent, BorderThickness = new Thickness(0), TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(20, 20, 20, 0), AcceptsReturn = false,
        };
        // Bare text as on the phone: the theme's focus and hover boxes off, the app's red caret.
        foreach (var key in new[] { "TextControlBackgroundFocused", "TextControlBackgroundPointerOver", "TextControlBorderBrushFocused", "TextControlBorderBrushPointerOver" })
            field.Resources[key] = Brushes.Transparent;
        field.CaretBrush = rows.Colour("spark_red_500");
        var count = rows.Text("", 14, white30, align: HorizontalAlignment.Center);
        count.Margin = new Thickness(20);
        void Changed()
        {
            var length = field.Text?.Trim().Length ?? 0;
            count.Text = $"{field.Text?.Length ?? 0}/{AmpController.MaxAmpName}";
            done.Opacity = length > 0 ? 1 : 0.3;
        }
        void Submit()
        {
            var newName = field.Text?.Trim() ?? "";
            if (newName.Length == 0) return;
            menu.Pop();
            if (newName != name) menu.Run("rename the amp", () => menu.Amp.RenameAmpAsync(newName));
        }
        field.TextChanged += (_, _) => Changed();
        field.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) Submit();
            if (e.Key == Key.Escape) menu.Pop();
        };
        MenuRows.Tap(cancel, menu.Pop);
        MenuRows.Tap(done, Submit);
        Changed();
        field.AttachedToVisualTree += (_, _) =>
        {
            field.Focus();
            field.SelectAll();
        };

        var label = rows.Text(text.Text("profile_devicedetail_rename_title"), 16, white30, semiBold: true, HorizontalAlignment.Center);
        return new DockPanel
        {
            Background = rows.Colour("grey950"),
            Children =
            {
                new Grid { Children = { cancel, done }, [DockPanel.DockProperty] = Dock.Top },
                new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { label, field, count } },
            },
        };
    }
}
