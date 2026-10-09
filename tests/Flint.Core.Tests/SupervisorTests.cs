using Flint.Core.Connection;
using Flint.Core.Protocol;
using Flint.Core.Session;

namespace Flint.Core.Tests;

// One test per arrow of the supervisor's state machine, on fake links and a clock shrunk to milliseconds.
public sealed class SupervisorTests : IAsyncDisposable
{
    private static readonly TimeSpan Ms = TimeSpan.FromMilliseconds(1);
    private static readonly SupervisorTimings Fast = new(100 * Ms, 30 * Ms, 40 * Ms, 3, [10 * Ms, 20 * Ms, 30 * Ms, 40 * Ms]);

    private readonly List<FakeLink> _links = [];
    private readonly List<LinkState> _states = [];
    private readonly List<TimeSpan> _retries = [];
    private Func<int, FakeLink> _makeLink = _ => Answering();
    private Func<AmpSession, CancellationToken, Task> _sync = (_, _) => Task.CompletedTask;
    private Supervisor? _supervisor;

    public async ValueTask DisposeAsync()
    {
        if (_supervisor is not null) await _supervisor.DisposeAsync();
    }

    [Fact]
    public async Task Start_connects_syncs_and_is_ready()
    {
        var synced = false;
        _sync = async (session, ct) => synced = (await session.RequestAsync(Opcode.GetSystemInfo, cancellationToken: ct)).Op == Opcode.SystemInfo;

        await Until(LinkState.Ready);

        Assert.True(synced);
        Assert.Equal([LinkState.Connecting, LinkState.Syncing, LinkState.Ready], Snapshot(_states));
        Assert.NotNull(_supervisor!.Session);
    }

    [Fact]
    public async Task A_failed_connect_backs_off_and_tries_again_with_a_new_link()
    {
        _makeLink = n => n < 2 ? new FakeLink { ConnectError = new IOException("not found") } : Answering();

        await Until(LinkState.Ready);

        Assert.Equal(3, _links.Count);
        Assert.All(_links.Take(2), l => Assert.True(l.Disposed));
        Assert.Equal([LinkState.Connecting, LinkState.Backoff, LinkState.Connecting, LinkState.Backoff,
            LinkState.Connecting, LinkState.Syncing, LinkState.Ready], Snapshot(_states));
    }

    [Fact]
    public async Task A_connect_that_hangs_times_out_into_backoff()
    {
        _makeLink = n => n == 0 ? new FakeLink { HangOnConnect = true } : Answering();

        await Until(LinkState.Ready);

        Assert.Contains("not connected within", _supervisor!.LastError);
        Assert.Equal(LinkState.Backoff, Snapshot(_states)[1]);
    }

    [Fact]
    public async Task A_failed_sync_backs_off_and_tries_again()
    {
        var attempts = 0;
        _sync = (_, _) => ++attempts == 1 ? throw new TimeoutException("no reply to 0271") : Task.CompletedTask;

        await Until(LinkState.Ready);

        Assert.Equal([LinkState.Connecting, LinkState.Syncing, LinkState.Backoff, LinkState.Connecting, LinkState.Syncing,
            LinkState.Ready], Snapshot(_states));
    }

    [Fact]
    public async Task A_drop_reported_by_the_os_reconnects_on_a_new_link()
    {
        await Until(LinkState.Ready);
        _states.Clear();
        _links[0].Drop("BlueZ reports it disconnected");

        await Until(LinkState.Ready, start: false, after: () => _links.Count == 2);

        Assert.Equal(2, _links.Count);
        Assert.True(_links[0].Disposed);
        Assert.Equal([LinkState.Backoff, LinkState.Connecting, LinkState.Syncing, LinkState.Ready], Snapshot(_states));
    }

    [Fact]
    public async Task Three_unanswered_keepalives_count_as_a_drop()
    {
        _makeLink = n => n == 0 ? new FakeLink() : Answering(); // the first amp goes silent
        await Until(LinkState.Ready);

        await Until(LinkState.Ready, start: false, after: () => _links.Count == 2);

        Assert.Equal(3, _links[0].Written.Count(c => c.Op == Opcode.GetSystemInfo));
        Assert.Contains("keepalives", _supervisor!.LastError);
    }

    [Fact]
    public async Task Answered_keepalives_keep_it_ready()
    {
        await Until(LinkState.Ready);
        await Task.Delay(Fast.KeepaliveInterval * 6);

        Assert.Single(_links);
        Assert.Equal(LinkState.Ready, _supervisor!.State);
        Assert.InRange(_links[0].Written.Count(c => c.Op == Opcode.GetSystemInfo), 2, 6);
    }

    [Fact]
    public async Task Traffic_from_the_amp_stands_in_for_keepalives_and_silence_brings_them_back()
    {
        await Until(LinkState.Ready);
        using var chatter = new CancellationTokenSource();
        var tuner = Task.Run(async () =>
        {
            while (!chatter.IsCancellationRequested)
            {
                _links[0].Send(new Chunk(0x40, 0x03, 0x64, [0x05, 0xCA, 0xBF, 0x80, 0x00, 0x00])); // a tuner reading
                await Task.Delay(5);
            }
        });
        await Task.Delay(Fast.KeepaliveInterval * 5);
        var whileChatting = _links[0].Written.Count(c => c.Op == Opcode.GetSystemInfo);
        await chatter.CancelAsync();
        await tuner;
        await Task.Delay(Fast.KeepaliveInterval * 5);

        Assert.InRange(whileChatting, 0, 1); // at most the one already due when the chatter began
        Assert.InRange(_links[0].Written.Count(c => c.Op == Opcode.GetSystemInfo) - whileChatting, 3, 5);
    }

    [Fact]
    public async Task It_never_gives_up_and_the_backoff_stops_growing_at_its_last_step()
    {
        _makeLink = _ => new FakeLink { ConnectError = new IOException("not found") };

        Start();
        await Wait.Until(() => _retries.Count >= 7);

        Assert.Equal(Fast.Backoff.Concat(Enumerable.Repeat(Fast.Backoff[^1], 3)), Snapshot(_retries).Take(7));
    }

    [Fact]
    public async Task Backoff_starts_again_from_its_first_step_after_a_good_connection()
    {
        _makeLink = n => n is 0 or 1 ? new FakeLink { ConnectError = new IOException("not found") } : Answering();
        await Until(LinkState.Ready);
        _links[^1].Drop("test");

        await Until(LinkState.Ready, start: false, after: () => _links.Count == 4);

        Assert.Equal([Fast.Backoff[0], Fast.Backoff[1], Fast.Backoff[0]], Snapshot(_retries));
    }

    [Fact]
    public async Task Stop_disconnects_and_goes_idle()
    {
        await Until(LinkState.Ready);

        await _supervisor!.StopAsync();

        Assert.Equal(LinkState.Idle, _supervisor.State);
        Assert.True(_links[0].Disposed);
        Assert.Null(_supervisor.Session);
        await Task.Delay(Fast.Backoff[^1] * 2);
        Assert.Single(_links);
    }

    [Fact]
    public async Task Stop_is_idle_at_once_while_the_link_winds_down_and_a_start_meanwhile_connects_after_it()
    {
        _sync = (_, _) => Task.Delay(300); // a step that does not stop when asked, as a BlueZ connect or scan does not

        await Until(LinkState.Syncing);
        var stopping = _supervisor!.StopAsync();

        Assert.Equal(LinkState.Idle, _supervisor.State); // the Cancel button answers at once
        Assert.False(stopping.IsCompleted);
        _supervisor.Start(); // Connect, pressed before the old link has gone
        Assert.Equal(LinkState.Connecting, _supervisor.State); // and it answers at once too
        await stopping;
        await Until(LinkState.Ready, start: false, after: () => _links.Count == 2);
    }

    private static FakeLink Answering()
    {
        var link = new FakeLink();
        link.Respond = w => w.Op == Opcode.GetSystemInfo ? [new Chunk(w.Seq, 0x03, 0x71, [6, 0, 1, 1, 0, 0, 0xD0, 0x9D])] : [];
        return link;
    }

    private void Start()
    {
        _supervisor = new Supervisor(() =>
        {
            lock (_links)
            {
                var link = _makeLink(_links.Count);
                _links.Add(link);
                return link;
            }
        }, (s, ct) => _sync(s, ct), Fast);
        _supervisor.StateChanged += state =>
        {
            lock (_states)
            {
                _states.Add(state);
                if (state == LinkState.Backoff) _retries.Add(_supervisor.RetryIn);
            }
        };
        _supervisor.Start();
    }

    private async Task Until(LinkState state, bool start = true, Func<bool>? after = null)
    {
        if (start) Start();
        await Wait.Until(() => (after?.Invoke() ?? true) && _supervisor!.State == state);
    }

    private List<T> Snapshot<T>(List<T> list)
    {
        lock (_states) return [.. list];
    }
}
