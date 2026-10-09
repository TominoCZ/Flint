using Flint.Core.Protocol.Payloads;

namespace Flint.Core.Model;

/// <summary>
/// The Simple Looper as the app runs it (CommonLooperViewModelImpl, QuickLooperViewModelImpl): the amp reports each step
/// in <c>03 75</c> and the screen follows; our own actions are applied once acked. Pure, so the window and tests share it.
/// </summary>
/// <param name="Dubbed">A dub has ended since the last reset, so undo is possible.</param>
/// <param name="Progress">Through the loop, 0 to 1 (<c>03 77</c>), while it plays.</param>
/// <param name="Timing">Tempo, length and count-in (<c>03 76</c>), null until the amp has sent it, or the owner's choice.</param>
/// <param name="RetryAfterCancel">Our Retry cancelled the recording; when the amp confirms, recording starts again.</param>
public sealed record Looper(
    LooperPhase Phase, UndoState Undo, bool Dubbed, float Progress, LooperTimeSignature? Timing, bool RetryAfterCancel)
{
    public static readonly Looper Initial = new(LooperPhase.Ready, UndoState.Unavailable, false, 0, null, false);

    /// <summary>The owner set <see cref="Timing"/> on the settings page, so the amp's own reports no longer replace it.</summary>
    public bool TimingChosen { get; init; }

    /// <summary>With the click on, a recording starts with a count-in; without it, at once.</summary>
    private LooperAction StartAction => Timing?.CountIn == false ? LooperAction.StartPunchIn : LooperAction.StartCountIn;

    /// <summary>What a button sends now, in order; empty if it does nothing in this phase.</summary>
    public IReadOnlyList<LooperAction> ActionsFor(LooperButton button) => (button, Phase) switch
    {
        (LooperButton.Main, LooperPhase.Ready) => [StartAction],
        (LooperButton.Main, LooperPhase.Recording) => [LooperAction.StopRecording],
        (LooperButton.Main, LooperPhase.Playing or LooperPhase.Stopped) => [LooperAction.DubStarted],
        (LooperButton.Main, LooperPhase.Dubbing) => [LooperAction.DubStopped],
        (LooperButton.Bottom, LooperPhase.Recording) => [LooperAction.CancelRecording],
        (LooperButton.Bottom, LooperPhase.Playing) => [LooperAction.PlaybackStopped],
        (LooperButton.Bottom, LooperPhase.Stopped) => [LooperAction.PlaybackStarted],
        (LooperButton.UndoRedo, LooperPhase.Playing or LooperPhase.Stopped) when Dubbed && Undo != UndoState.Unavailable
            => [Undo == UndoState.Undo ? LooperAction.Undo : LooperAction.Redo],
        (LooperButton.Discard, _) => [LooperAction.ClearPlayback],
        (LooperButton.Close, LooperPhase.CountIn or LooperPhase.PunchIn or LooperPhase.Recording) => [LooperAction.CancelRecording],
        (LooperButton.Close, LooperPhase.Playing) => [LooperAction.PlaybackStopped],
        (LooperButton.Close, LooperPhase.Dubbing) => [LooperAction.DubStopped, LooperAction.PlaybackStopped],
        _ => [],
    };

    /// <summary>The state once a button's action has gone out and been acked: only a Retry needs remembering.</summary>
    public Looper Sent(LooperButton button, LooperAction action) =>
        Reported(action, byUs: true) with { RetryAfterCancel = button == LooperButton.Bottom && action == LooperAction.CancelRecording };

    /// <summary>
    /// The state after the amp reported <paramref name="action"/> (<c>03 75</c>; <paramref name="initial"/> for the
    /// <c>02 75</c> read when the page opens), or after our own action was acked (<paramref name="byUs"/>). Our own
    /// count-in and cancel change nothing until the amp reports them.
    /// </summary>
    public Looper Reported(LooperAction action, bool byUs = false, bool initial = false) => action switch
    {
        LooperAction.ResetToReady when !initial => Reset(),
        LooperAction.StartCountIn when !byUs => this with { Phase = Phase == LooperPhase.Stopped ? LooperPhase.StoppedCountIn : LooperPhase.CountIn },
        LooperAction.StartPunchIn => this with { Phase = LooperPhase.PunchIn },
        LooperAction.StartRecording => this with { Phase = LooperPhase.Recording },
        LooperAction.CancelRecording when !byUs => Reset(),
        LooperAction.PlaybackStarted when Phase != LooperPhase.Dubbing => Playing(),
        LooperAction.PlaybackStopped => this with { Phase = LooperPhase.Stopped, Progress = 0 },
        LooperAction.DubStarted => this with { Phase = LooperPhase.Dubbing, Undo = UndoState.Unavailable },
        LooperAction.DubStopped when Phase != LooperPhase.Stopped => (this with { Dubbed = true }).Playing(),
        LooperAction.Undo or LooperAction.Redo => this with { Undo = Undo == UndoState.Undo ? UndoState.Redo : UndoState.Undo },
        _ => this,
    };

    /// <summary>
    /// What to send after the amp reported <paramref name="reported"/>, asked before applying it: a cancel that was our
    /// Retry starts the recording again.
    /// </summary>
    public LooperAction? FollowUp(LooperAction reported) =>
        reported == LooperAction.CancelRecording && RetryAfterCancel ? StartAction : null;

    /// <summary>The amp's time signature (<c>03 76</c> after every slot switch, <c>02 76</c>), unless the owner chose one.</summary>
    public Looper Reported(LooperTimeSignature timing) => TimingChosen ? this : this with { Timing = timing };

    /// <summary>
    /// The settings page's choice, kept here as the app keeps it in its preferences: the app never sends <c>01 76</c> from
    /// that page (only on a control pedal's drum mute), and the owner's amp stopped answering altogether when we did.
    /// </summary>
    public Looper Chose(LooperTimeSignature timing) => this with { Timing = timing, TimingChosen = true };

    /// <summary>
    /// The settings page's choices as the app keeps them (QuickLooperViewModel's time signature): a free length
    /// (<paramref name="bars"/> 0) holds as many bars as fit in the amp's longest recording. The click off keeps the length
    /// as it was, greyed out, for when it comes back on.
    /// </summary>
    public static LooperTimeSignature TimingFor(uint bpm, bool click, uint bars, uint beats, uint maxSeconds)
    {
        var free = bars == 0;
        return new(bpm, beats, free ? (uint)(maxSeconds / (60.0 / bpm * beats)) : bars, free, click, false, maxSeconds);
    }

    /// <summary>The slowest tempo at which <paramref name="bars"/> bars still fit in the amp's longest recording, and never under 40.</summary>
    public static uint MinBpm(uint bars, uint beats, uint maxSeconds) => Math.Max(40, bars * beats * 60 / Math.Max(1, maxSeconds));

    private Looper Playing() => this with
    {
        Phase = LooperPhase.Playing,
        Undo = Dubbed && Undo == UndoState.Unavailable ? UndoState.Undo : Undo,
    };

    private Looper Reset() => Initial with { Timing = Timing, TimingChosen = TimingChosen };
}
