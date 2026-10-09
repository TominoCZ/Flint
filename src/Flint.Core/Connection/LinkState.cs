namespace Flint.Core.Connection;

/// <summary>The supervisor's states. The window shows them: green Ready, amber otherwise, with the countdown in Backoff.</summary>
public enum LinkState
{
    Idle,
    Connecting,
    Syncing,
    Ready,
    Backoff,
}
