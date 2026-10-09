using Flint.Core.Logging;
using Flint.Core.Protocol;
using Flint.Core.Session;

namespace Flint.Core.Connection;

/// <summary>
/// Keeps one amp connected for as long as it runs: connect, sync, hold, and on any failure back off and
/// start again with a fresh link. It never gives up. A drop is whatever comes first of the OS saying so and
/// <see cref="SupervisorTimings.MissedKeepalives"/> unanswered <c>02 71</c> reads, because the OS is sometimes late or silent.
/// </summary>
/// <param name="newLink">Makes the link for one attempt; the supervisor disposes it when the attempt ends.</param>
/// <param name="sync">The handshake that fills the app's state from a new session. Throwing fails the attempt.</param>
public sealed class Supervisor(Func<ILink> newLink, Func<AmpSession, CancellationToken, Task> sync, SupervisorTimings? timings = null)
    : IAsyncDisposable
{
    private readonly SupervisorTimings _timings = timings ?? SupervisorTimings.Default;
    private CancellationTokenSource? _stop;
    private Task? _loop;

    public LinkState State { get; private set; }

    /// <summary>The session in <see cref="LinkState.Syncing"/> and <see cref="LinkState.Ready"/>, null otherwise.</summary>
    public AmpSession? Session { get; private set; }

    /// <summary>Why the last attempt ended, kept after a reconnect so the window can say what happened.</summary>
    public string? LastError { get; private set; }

    /// <summary>In <see cref="LinkState.Backoff"/>: how long until the next attempt.</summary>
    public TimeSpan RetryIn { get; private set; }

    /// <summary>Raised on the supervisor's own thread; UI code posts it to the UI thread.</summary>
    public event Action<LinkState>? StateChanged;

    public void Start()
    {
        if (_loop is not null && _stop is { IsCancellationRequested: false }) return;
        // A stop may still be winding down (a BlueZ connect or scan ends in its own time): start after it.
        var previous = _loop ?? Task.CompletedTask;
        var stop = _stop = new CancellationTokenSource();
        Enter(LinkState.Connecting, stop.Token); // the window answers Connect at once, even while waiting for that stop
        _loop = Task.Run(async () =>
        {
            await previous;
            await RunAsync(stop.Token);
        });
    }

    /// <summary>
    /// Disconnects cleanly, so the phone can take the amp. <see cref="LinkState.Idle"/> at once, so the window answers
    /// Cancel straight away; the task ends when the link has wound down.
    /// </summary>
    public async Task StopAsync()
    {
        if (_loop is not { } loop || _stop is not { } stop) return;
        await stop.CancelAsync();
        Enter(LinkState.Idle);
        await loop;
        if (ReferenceEquals(_loop, loop)) (_loop, _stop) = (null, null);
        stop.Dispose();
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    private async Task RunAsync(CancellationToken stop)
    {
        var failures = 0;
        while (true)
        {
            try
            {
                await AttemptAsync(stop, () => failures = 0);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                LastError = e.Message;
                Log.Warn("ble", $"{State.ToString().ToLowerInvariant()} failed: {e.Message}");
            }

            RetryIn = _timings.Backoff[Math.Min(failures++, _timings.Backoff.Count - 1)];
            Enter(LinkState.Backoff, stop);
            try
            {
                await Task.Delay(RetryIn, stop);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>One connection, from connect to its end. Only returns by throwing.</summary>
    private async Task AttemptAsync(CancellationToken stop, Action onReady)
    {
        var link = newLink();
        var dropped = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        link.Dropped += reason => dropped.TrySetResult(reason);
        try
        {
            Enter(LinkState.Connecting, stop);
            using (var connect = CancellationTokenSource.CreateLinkedTokenSource(stop))
            {
                connect.CancelAfter(_timings.ConnectTimeout);
                try
                {
                    await link.ConnectAsync(connect.Token);
                }
                catch (OperationCanceledException) when (!stop.IsCancellationRequested)
                {
                    throw new TimeoutException($"not connected within {_timings.ConnectTimeout.TotalSeconds:0.#} s");
                }
            }

            using var session = new AmpSession(link, _timings.RequestTimeout);
            Session = session;
            Enter(LinkState.Syncing, stop);
            await sync(session, stop);
            onReady();
            Enter(LinkState.Ready, stop);
            await HoldAsync(session, dropped.Task, stop);
        }
        finally
        {
            Session = null;
            await link.DisposeAsync();
        }
    }

    private async Task HoldAsync(AmpSession session, Task<string> dropped, CancellationToken stop)
    {
        var missed = 0;
        while (true)
        {
            var waitStarted = DateTime.UtcNow;
            var tick = Task.Delay(_timings.KeepaliveInterval, stop);
            if (await Task.WhenAny(dropped, tick) == dropped) throw new IOException($"link dropped: {await dropped}");
            await tick; // throws once stopping

            // Any traffic during the wait proves the link alive; only silence costs a read. (Comparing against "now"
            // instead skipped every other read on the amp: the wait starts just after the last reply.)
            if (session.LastReceived > waitStarted)
            {
                missed = 0;
                continue;
            }
            try
            {
                await session.RequestAsync(Opcode.GetSystemInfo, cancellationToken: stop);
                missed = 0;
            }
            catch (TimeoutException)
            {
                if (++missed >= _timings.MissedKeepalives) throw new IOException($"{missed} keepalives in a row went unanswered");
                Log.Warn("ble", $"keepalive {missed} of {_timings.MissedKeepalives} unanswered");
            }
        }
    }

    /// <param name="attempt">The run asking: once it is cancelled, it winds down (a failure or a backoff surfacing after
    /// Cancel) without undoing the Idle shown, or the Connecting of a run started since.</param>
    private void Enter(LinkState state, CancellationToken attempt = default)
    {
        if (attempt.IsCancellationRequested || state == State) return;
        State = state;
        Log.Info("ble", state == LinkState.Backoff ? $"retry in {RetryIn.TotalSeconds:0.#} s" : state.ToString().ToLowerInvariant());
        StateChanged?.Invoke(state);
    }
}
