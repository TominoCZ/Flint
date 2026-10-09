namespace Flint.Core.Model;

/// <summary>
/// The looper's actions and states (the app's <c>SparkSysInfo.LooperAction</c>): we send one with <c>01 75</c>, and the
/// amp reports where it is with the same codes in <c>03 75</c>.
/// </summary>
public enum LooperAction
{
    ResetToReady = 0,
    ExitLooper = 1,
    StartCountIn = 2,
    StartPunchIn = 3,
    StartRecording = 4,
    StopRecording = 5,
    CancelRecording = 6,
    RecordingEnded = 7,
    PlaybackStarted = 8,
    PlaybackStopped = 9,
    ClearPlayback = 10,
    DubStarted = 11,
    DubStopped = 12,
    Undo = 13,
    Redo = 14,
}
