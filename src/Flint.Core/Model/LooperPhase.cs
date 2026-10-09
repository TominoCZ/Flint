namespace Flint.Core.Model;

/// <summary>What the Simple Looper's screen shows: the app's looper UI states (<c>i6.z</c>, <c>x</c>, <c>E</c>, …).</summary>
public enum LooperPhase
{
    Ready,
    CountIn,
    PunchIn,
    Recording,
    Playing,
    Stopped,
    Dubbing,

    /// <summary>A count-in started from a stopped loop.</summary>
    StoppedCountIn,
}
