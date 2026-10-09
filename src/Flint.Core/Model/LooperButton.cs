namespace Flint.Core.Model;

/// <summary>The Simple Looper's buttons (<c>fragment_quick_looper.xml</c>).</summary>
public enum LooperButton
{
    /// <summary>The big round button: record, stop recording, dub.</summary>
    Main,

    /// <summary>The wide button below: retry while recording, stop or play the loop.</summary>
    Bottom,
    UndoRedo,
    Discard,

    /// <summary>Leaving the page, which stops whatever runs, as the app does.</summary>
    Close,
}
