using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Flint.App.Assets;
using Flint.Core.Presets;

namespace Flint.App.Tone;

/// <summary>
/// The app's name dialog for Save as New and Save to hardware preset (<c>popup_edit_preset.xml</c>): the name, up to 40
/// characters, starting as the preset's own; for a hardware save, the slot under it, which opens the slot picker
/// (<c>popup_string_picker.xml</c>) and starts at CH1A whatever the current slot, as the app does. Save is enabled
/// while the name is not blank. The preset icon the app draws at the top is left out.
/// </summary>
internal sealed class PresetNameDialog : Grid
{
    private static readonly IBrush Card = new SolidColorBrush(Color.Parse("#1c1c1c")), Field = new SolidColorBrush(Color.Parse("#73141414")),
        FieldEdge = new SolidColorBrush(Color.Parse("#73373737")), Red = new SolidColorBrush(Color.Parse("#e02020")),
        PickerText = new SolidColorBrush(Color.Parse("#de000000")), Purple = new SolidColorBrush(Color.Parse("#673ab7"));

    private readonly AndroidValues _values;
    private readonly Func<int, string> _label;
    private readonly Button _slotButton;
    private int? _slot;

    /// <param name="slots">How many slots to offer, or 0 for a name only.</param>
    /// <param name="label">A slot's label, "CH1A" for the first.</param>
    /// <param name="action">The confirm button's text: Save, or Rename from the ▾ list.</param>
    public PresetNameDialog(AndroidValues values, string name, int slots, Func<int, string> label, ScaleTransform scale, string? action = null)
    {
        (_values, _label) = (values, label);
        Background = new SolidColorBrush(Color.Parse("#99000000"));
        NameField = new TextBox
        {
            Text = name.Length > Preset.MaxNameLength ? name[..Preset.MaxNameLength] : name, MaxLength = Preset.MaxNameLength,
            Height = 40, Margin = new Thickness(12, 27, 12, 0), Padding = new Thickness(21, 0), FontSize = 14, FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.White, Background = Field, BorderBrush = FieldEdge, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(19), VerticalContentAlignment = VerticalAlignment.Center, AcceptsReturn = false,
        };
        _slot = slots > 0 ? 0 : null;
        _slotButton = new Button
        {
            IsVisible = slots > 0, Height = 40, Margin = new Thickness(12, 20, 12, 13), Padding = new Thickness(21, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Center, FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White,
            Background = Field, BorderBrush = FieldEdge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(19),
            Content = slots > 0 ? label(0) : "",
        };
        _slotButton.Click += (_, _) => OpenPicker(slots, scale);

        var cancel = RoundButton(values.Text("cancel"), null);
        var save = RoundButton(action ?? values.Text("save"), Red);
        cancel.Margin = new Thickness(0, 0, 9, 0);
        save.Margin = new Thickness(9, 0, 0, 0);
        save[Grid.ColumnProperty] = 1;
        save.IsEnabled = !string.IsNullOrWhiteSpace(NameField.Text);
        NameField.TextChanged += (_, _) => save.IsEnabled = !string.IsNullOrWhiteSpace(NameField.Text);
        cancel.Click += (_, _) => Closed?.Invoke();
        save.Click += (_, _) => Submit();
        NameField.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter && save.IsEnabled) Submit();
            if (e.Key == Avalonia.Input.Key.Escape) Closed?.Invoke();
        };

        var card = new StackPanel { Children = { NameField, _slotButton, new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), Margin = new Thickness(12, 24, 12, 35), Children = { cancel, save } } } };
        Children.Add(new LayoutTransformControl
        {
            LayoutTransform = scale, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
            Child = new Border { Width = EditorHost.DesignWidth - 20, Background = Card, CornerRadius = new CornerRadius(4), Child = card },
        });
        AttachedToVisualTree += (_, _) =>
        {
            NameField.Focus();
            NameField.SelectAll();
        };
    }

    /// <summary>The name field, for tests.</summary>
    public TextBox NameField { get; }

    /// <summary>Save: the trimmed name, and the slot for a hardware save. The dialog has closed.</summary>
    public event Action<string, int?>? Saved;

    public event Action? Closed;

    private void Submit()
    {
        if (string.IsNullOrWhiteSpace(NameField.Text)) return;
        Closed?.Invoke();
        Saved?.Invoke(NameField.Text.Trim(), _slot);
    }

    /// <summary>The slot picker: a white card titled CHOOSE YOUR PRESET with one radio button per slot, Cancel and Ok.</summary>
    private void OpenPicker(int slots, ScaleTransform scale)
    {
        var radios = Enumerable.Range(0, slots).Select(i => new RadioButton
        {
            Content = _label(i), IsChecked = i == _slot, Foreground = PickerText, FontFamily = SparkFonts.Roboto, FontSize = 16, Height = 48,
            GroupName = "slot",
        }).ToList();
        var ok = PickerButton(_values.Text("ok"));
        var cancel = PickerButton(_values.Text("cancel"));
        var picker = new Grid { Background = Brushes.Transparent };
        ok.Click += (_, _) =>
        {
            _slot = radios.FindIndex(r => r.IsChecked == true) is var i and >= 0 ? i : _slot;
            _slotButton.Content = _label(_slot ?? 0);
            Children.Remove(picker);
        };
        cancel.Click += (_, _) => Children.Remove(picker);
        var list = new StackPanel();
        list.Children.AddRange(radios);
        picker.Children.Add(new LayoutTransformControl
        {
            LayoutTransform = scale, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
            Child = new ThemeVariantScope { RequestedThemeVariant = ThemeVariant.Light, Child = new StackPanel // dark radio rings on white
            {
                Width = EditorHost.DesignWidth - 80, Background = Brushes.White,
                Children =
                {
                    new TextBlock
                    {
                        Text = _values.Text("preset_picker_title"), FontSize = 20, FontFamily = SparkFonts.Roboto, FontWeight = FontWeight.Medium,
                        Foreground = PickerText, TextAlignment = TextAlignment.Center, Margin = new Thickness(24, 21, 24, 19),
                    },
                    new ScrollViewer { Height = 192, Padding = new Thickness(24, 0), Content = list },
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, ok } },
                },
            } },
        });
        Children.Add(picker);
    }

    private static Button RoundButton(string text, IBrush? fill) => new()
    {
        Content = text.ToUpperInvariant(), Height = 33, FontSize = 14, FontWeight = FontWeight.Medium, Foreground = Brushes.White,
        HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center, CornerRadius = new CornerRadius(16.5),
        Background = fill ?? Brushes.Transparent, BorderBrush = fill ?? Brushes.White, BorderThickness = new Thickness(1),
    };

    private static Button PickerButton(string text) => new()
    {
        Content = text.ToUpperInvariant(), FontSize = 14, FontFamily = SparkFonts.Roboto, FontWeight = FontWeight.Medium,
        Foreground = Purple, Background = Brushes.Transparent, Margin = new Thickness(0, 0, 8, 8),
    };
}
