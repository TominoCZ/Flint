using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Flint.App.Animation;

namespace Flint.App.Controls;

/// <summary>
/// Plays a <see cref="LottieAnimation"/> in a loop, fitted and centred, as <c>LottieAnimationView</c> does. Pausing is
/// smooth: it slows to a stop while fading out, then rests on the first frame; playing again fades in from there
/// while it speeds up.
/// </summary>
internal sealed class LottieView : Control
{
    private const double EaseSeconds = 0.25; // a 63% step towards stopped or playing; about a second to settle

    private readonly LottieAnimation _animation;
    private double _seconds, _speed = 1, _fade = 1; // the animation's own clock, and how fast and how visible it runs
    private TimeSpan? _lastFrame;
    private bool _paused, _attached, _running;

    public LottieView(LottieAnimation animation) => _animation = animation;

    /// <summary>The frame drawn now: the animation's clock at the file's frame rate, looped between its in and out frames.</summary>
    public double Frame => _animation.InFrame + _seconds * _animation.FrameRate % (_animation.OutFrame - _animation.InFrame);

    /// <summary>Slows to a stop and fades out, or with false, fades in and plays again.</summary>
    public bool Paused
    {
        get => _paused;
        set
        {
            _paused = value;
            Run();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        Run();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        (_attached, _running, _lastFrame) = (false, false, null);
        base.OnDetachedFromVisualTree(e);
    }

    public override void Render(DrawingContext context)
    {
        var scale = Math.Min(Bounds.Width / _animation.Width, Bounds.Height / _animation.Height);
        if (scale <= 0 || _fade <= 0.001) return;
        var x = (Bounds.Width - _animation.Width * scale) / 2;
        var y = (Bounds.Height - _animation.Height * scale) / 2;
        using (context.PushOpacity(_fade))
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(x, y)))
            _animation.Render(context, Frame);
    }

    private void Run()
    {
        if (_running || !_attached) return;
        _running = true;
        TopLevel.GetTopLevel(this)?.RequestAnimationFrame(Step);
    }

    private void Step(TimeSpan now)
    {
        if (!_attached) return;
        var seconds = _lastFrame is { } last ? Math.Clamp((now - last).TotalSeconds, 0, 0.1) : 0;
        _lastFrame = now;
        var target = _paused ? 0 : 1;
        var ease = 1 - Math.Exp(-seconds / EaseSeconds);
        _speed += (target - _speed) * ease;
        _fade += (target - _fade) * ease;
        _seconds += seconds * _speed;
        InvalidateVisual();
        if (_paused && _fade < 0.01)
        {
            // At rest: invisible, back at the first frame, ready to start from there.
            (_seconds, _speed, _fade, _running, _lastFrame) = (0, 0, 0, false, null);
            return;
        }
        TopLevel.GetTopLevel(this)?.RequestAnimationFrame(Step);
    }
}
