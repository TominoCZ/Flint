namespace Flint.Core.Connection;

/// <summary>The supervisor's clock. Tests shrink every value to milliseconds.</summary>
public sealed record SupervisorTimings(
    TimeSpan ConnectTimeout,
    TimeSpan RequestTimeout,
    TimeSpan KeepaliveInterval,
    int MissedKeepalives,
    IReadOnlyList<TimeSpan> Backoff)
{
    /// <summary>Connect within 20 s, read <c>02 71</c> after 5 s of silence, 3 misses is a drop, retry after 1, 2, 5, then every 10 s.</summary>
    public static readonly SupervisorTimings Default = new(
        TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), 3,
        [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)]);
}
