using Avalonia.Controls;
using Avalonia.Media;
using Flint.App.Menu;
using Flint.App.Tone;
using Flint.Core.Model;

namespace Flint.App.Controls;

/// <summary>
/// A page of a tab: what it shows of the amp (it is rebuilt only when that changes), how to build it, and, for a page
/// that must not be rebuilt while it runs (the looper), how to bring it up to date instead.
/// </summary>
internal sealed record TabPage(Func<AmpState, object> Key, Func<AmpState, Control> Build, Action<AmpState>? Update = null);

/// <summary>
/// A tab whose pages open over each other and close with their back arrow (the Menu and Music tabs), laid out in the
/// phone's dp and scaled with the window, as the Tone screen is.
/// </summary>
internal class PageStack : Grid
{
    private readonly List<TabPage> _pages = [];
    private readonly ContentControl _host = new();
    private readonly ScaleTransform _scale = new(1, 1);
    private TabPage? _shownPage;
    private object? _shownKey;

    /// <param name="run">Runs an amp action and reports its failure, as the window does for its own buttons.</param>
    protected PageStack(MenuRows rows, AmpController amp, Action<string, Func<Task>> run)
    {
        (Rows, Amp, Run) = (rows, amp, run);
        Background = rows.Colour("grey950");
        ClipToBounds = true;
        Children.Add(new LayoutTransformControl { Child = _host, LayoutTransform = _scale });
        SizeChanged += (_, e) => _scale.ScaleX = _scale.ScaleY = ToneScreen.Scale(e.NewSize.Height);
    }

    public MenuRows Rows { get; }
    public AmpController Amp { get; }
    public Action<string, Func<Task>> Run { get; }

    /// <summary>The page on top, for tests.</summary>
    public Control? Shown => _host.Content as Control;

    public void Push(TabPage page)
    {
        _pages.Add(page);
        Show();
    }

    public void Pop()
    {
        if (_pages.Count > 1) _pages.RemoveAt(_pages.Count - 1);
        Show();
    }

    /// <summary>Back to the tab's first page, as tapping the tab does on the phone.</summary>
    public void Home()
    {
        if (_pages.Count > 1) _pages.RemoveRange(1, _pages.Count - 1);
        Show();
    }

    /// <summary>Shows the page on top with the amp's state, rebuilding it only if what it shows has changed.</summary>
    public void Show()
    {
        if (Amp.State is not { } state || _pages.Count == 0) return;
        var page = _pages[^1];
        var key = page.Key(state);
        if (!ReferenceEquals(page, _shownPage) || !Equals(key, _shownKey))
        {
            (_shownPage, _shownKey) = (page, key);
            _host.Content = page.Build(state);
        }
        page.Update?.Invoke(state);
    }

    /// <summary>A page's rows, scrolling when they do not fit.</summary>
    public static Control Scrolling(params Control[] rows)
    {
        var list = new StackPanel();
        list.Children.AddRange(rows);
        return new ScrollViewer { Content = list, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    }
}
