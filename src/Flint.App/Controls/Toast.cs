using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Flint.App.Controls;

/// <summary>The app's toast: a line at the foot of the screen for a few seconds, after a save or for a refusal.</summary>
internal sealed class Toast : Border
{
    private readonly TextBlock _text = new() { FontSize = 14, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
    private IDisposable? _timer;

    public Toast()
    {
        Background = new SolidColorBrush(Color.Parse("#e6202020"));
        CornerRadius = new CornerRadius(20);
        Padding = new Thickness(20, 10);
        Margin = new Thickness(20, 0, 20, 40);
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Bottom;
        IsVisible = false;
        IsHitTestVisible = false;
        ZIndex = 2;
        Child = _text;
    }

    public void Show(string text)
    {
        _text.Text = text;
        IsVisible = true;
        _timer?.Dispose();
        _timer = DispatcherTimer.RunOnce(() => IsVisible = false, TimeSpan.FromSeconds(3));
    }
}
