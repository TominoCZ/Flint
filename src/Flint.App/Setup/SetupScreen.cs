using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Flint.Core.Logging;

namespace Flint.App.Setup;

/// <summary>
/// The setup screen: takes the Spark app's artwork from the user's phone, or from an .apkm file, step by step, each
/// step saying what it is doing or what the user needs to do. Plain controls in the app's dark theme, since the Spark
/// artwork it fetches is not there yet. Shown on a first run and from the tray's "Update Spark artwork…".
/// </summary>
internal sealed class SetupScreen : UserControl
{
    private static readonly IBrush Grey = Brush.Parse("#9a9a9a"), Blue = Brush.Parse("#58a6ff"), Green = Brush.Parse("#3fb950"),
                                   Amber = Brush.Parse("#d29922"), Red = Brush.Parse("#f85149");

    private readonly string _target;
    private readonly bool _update;
    private readonly StackPanel _steps = new() { Spacing = 18 };
    private readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal, Spacing = 10 };
    private readonly Button _useFile = new()
    {
        Content = "No Android phone? Use an .apkm file instead…", Background = Brushes.Transparent, Foreground = Blue, Padding = new Thickness(0),
    };
    private SetupFlow? _flow;
    private CancellationTokenSource? _cancel;

    /// <param name="target">The assets folder to fill (or replace).</param>
    /// <param name="update">True when the app already has artwork: the screen can then be closed, and says "update".</param>
    public SetupScreen(string target, bool update)
    {
        (_target, _update) = (target, update);
        Background = Brush.Parse("#101010");
        _useFile.Click += async (_, _) => await ChooseFileAsync();
        var intro = new TextBlock
        {
            Text = "Flint draws its screens with the Spark app's own artwork, which it takes from the copy on your Android phone. " +
                   "Nothing on the phone changes, and nothing leaves this computer.",
            TextWrapping = TextWrapping.Wrap, Foreground = Grey, FontSize = 14,
        };
        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                MaxWidth = 680, Margin = new Thickness(32, 40), Spacing = 22,
                Children =
                {
                    new TextBlock { Text = update ? "Update the Spark artwork" : "Set up Flint", FontSize = 26, FontWeight = FontWeight.SemiBold },
                    intro, _steps, _buttons, _useFile,
                },
            },
        };
        DetachedFromVisualTree += (_, _) => _cancel?.Cancel();
    }

    /// <summary>The user closed the screen (only while updating, when there is artwork to go back to).</summary>
    public event Action? Closed;

    /// <summary>The artwork is in place; the app restarts to use it.</summary>
    public event Action? Restart;

    /// <summary>Starts taking the artwork from the phone, or from <paramref name="apkm"/>.</summary>
    public void Start(string? apkm = null)
    {
        _cancel?.Cancel();
        _cancel = new CancellationTokenSource();
        var flow = _flow = new SetupFlow(_target, apkm);
        flow.Changed += () => Dispatcher.UIThread.Post(() =>
        {
            if (flow == _flow) Show(flow.Steps, flow.Finished); // not one that was replaced
        });
        Show(flow.Steps, flow.Finished);
        _ = flow.RunAsync(_cancel.Token);
    }

    /// <summary>Draws the steps and the buttons that fit where they are. Internal for the tests, which draw made-up states.</summary>
    internal void Show(IReadOnlyList<SetupStep> steps, bool finished)
    {
        var failed = steps.Any(s => s.State == StepState.Failed);
        _steps.Children.Clear();
        foreach (var step in steps) _steps.Children.Add(Row(step));

        _buttons.Children.Clear();
        if (finished)
            _buttons.Children.Add(Action("Start Flint with it", () => Restart?.Invoke(), accent: true));
        if (failed)
            _buttons.Children.Add(Action("Try again", () => Start(), accent: true));
        if (_update && !finished)
            _buttons.Children.Add(Action(failed ? "Close" : "Cancel", () =>
            {
                _cancel?.Cancel();
                Closed?.Invoke();
            }));
        _buttons.IsVisible = _buttons.Children.Count > 0;
        _useFile.IsVisible = !finished && steps.Count > 4; // the phone's way, not yet done
    }

    private Control Row(SetupStep step)
    {
        var (mark, colour) = step.State switch
        {
            StepState.Running => ("●", Blue),
            StepState.NeedsYou => ("!", Amber),
            StepState.Done => ("✓", Green),
            StepState.Failed => ("✕", Red),
            _ => ("○", Grey),
        };
        var text = new StackPanel { Spacing = 6 };
        text.Children.Add(new TextBlock
        {
            Text = step.Title, FontSize = 16, FontWeight = FontWeight.SemiBold,
            Foreground = step.State == StepState.Waiting ? Grey : Brushes.White,
        });
        if (step.Detail.Length > 0)
            text.Children.Add(new TextBlock
            {
                Text = step.Detail, TextWrapping = TextWrapping.Wrap, FontSize = 14,
                Foreground = step.State is StepState.NeedsYou or StepState.Failed ? colour : Grey,
            });
        if (step.Command is { } command) text.Children.Add(CommandBox(command));
        if (step.State == StepState.Running)
            text.Children.Add(new ProgressBar
            {
                Minimum = 0, Maximum = 1, Value = step.Progress ?? 0, IsIndeterminate = step.Progress is null, Height = 6, MinHeight = 6,
                Margin = new Thickness(0, 4, 0, 0),
            });
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("32,*") };
        row.Children.Add(new TextBlock { Text = mark, Foreground = colour, FontSize = 18, FontWeight = FontWeight.Bold });
        text[Grid.ColumnProperty] = 1;
        row.Children.Add(text);
        return row;
    }

    /// <summary>A terminal command, selectable, with a button that copies it.</summary>
    private Control CommandBox(string command)
    {
        var copy = new Button { Content = "Copy", VerticalAlignment = VerticalAlignment.Center };
        copy.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(command);
            copy.Content = "Copied";
        };
        var box = new TextBox
        {
            Text = command, IsReadOnly = true, FontFamily = new FontFamily("monospace"), FontSize = 14,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        copy[Grid.ColumnProperty] = 1;
        row.Children.Add(box);
        row.Children.Add(copy);
        return row;
    }

    private static Button Action(string label, Action click, bool accent = false)
    {
        var button = new Button { Content = label, Padding = new Thickness(18, 8) };
        if (accent) button.Classes.Add("accent");
        button.Click += (_, _) => click();
        return button;
    }

    private async Task ChooseFileAsync()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "The Spark app as an app bundle",
            FileTypeFilter = [new FilePickerFileType("Android app bundle") { Patterns = ["*.apkm", "*.apks", "*.xapk"] }],
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        Log.Info("setup", $"from the file {path}");
        Start(path);
    }
}
