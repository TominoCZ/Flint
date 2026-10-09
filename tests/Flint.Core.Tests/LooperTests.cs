using Flint.Core.Connection;
using Flint.Core.Library;
using Flint.Core.Model;
using Flint.Core.Protocol;
using Flint.Core.Protocol.Payloads;

namespace Flint.Core.Tests;

// The Simple Looper against the app's state machine (CommonLooperViewModelImpl): our presses send actions, the amp's
// 03 75 reports move the screen.
public sealed class LooperTests : IAsyncDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("flint-looper-").FullName;
    private readonly FakeAmp _amp = new();
    private FakeLink? _link;
    private AmpController? _controller;
    private byte _seq = 0x40;

    public async ValueTask DisposeAsync()
    {
        if (_controller is not null) await _controller.DisposeAsync();
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task A_loop_is_recorded_played_dubbed_and_undone_as_the_amp_reports_it()
    {
        var amp = await StartAsync();
        await amp.ReadLooperAsync();
        Assert.Equal((LooperPhase.Ready, 120u), (Phase(amp), amp.State!.Looper.Timing!.Bpm));

        await amp.PressLooperAsync(LooperButton.Main);
        Assert.Equal(LooperPhase.Ready, Phase(amp)); // our count-in shows only once the amp reports it
        await Report(amp, LooperAction.StartCountIn, LooperPhase.CountIn);
        await Report(amp, LooperAction.StartRecording, LooperPhase.Recording);
        await amp.PressLooperAsync(LooperButton.Main);
        await Report(amp, LooperAction.RecordingEnded, LooperPhase.Recording);
        await Report(amp, LooperAction.PlaybackStarted, LooperPhase.Playing);
        Assert.Equal(UndoState.Unavailable, amp.State.Looper.Undo);

        await amp.PressLooperAsync(LooperButton.Main);
        Assert.Equal(LooperPhase.Dubbing, Phase(amp));
        await amp.PressLooperAsync(LooperButton.Main);
        Assert.Equal((LooperPhase.Playing, UndoState.Undo), (Phase(amp), amp.State.Looper.Undo));
        await amp.PressLooperAsync(LooperButton.UndoRedo);
        Assert.Equal(UndoState.Redo, amp.State.Looper.Undo);

        await amp.PressLooperAsync(LooperButton.Close); // leaving the page stops the loop, as the app does
        Assert.Equal(LooperPhase.Stopped, Phase(amp));
        Assert.Equal([LooperAction.StartCountIn, LooperAction.StopRecording, LooperAction.DubStarted, LooperAction.DubStopped,
            LooperAction.Undo, LooperAction.PlaybackStopped], _amp.Looper);
    }

    [Fact]
    public async Task Retry_cancels_the_recording_and_starts_it_again_once_the_amp_confirms()
    {
        var amp = await StartAsync();
        amp.ChooseLooperTiming(new LooperTimeSignature(90, 4, 2, false, false, false, 60)); // click off: no count-in
        await Report(amp, LooperAction.StartRecording, LooperPhase.Recording);

        await amp.PressLooperAsync(LooperButton.Bottom);
        Send(LooperAction.CancelRecording); // the amp confirms: Ready for a moment, then the new start
        await Wait.Until(() => _amp.Looper.Count == 2);

        Assert.Equal([LooperAction.CancelRecording, LooperAction.StartPunchIn], _amp.Looper);
        Assert.Equal(120u, _amp.LooperTiming.Bpm); // the choice stays with us: no 01 76 reaches the amp
        await Wait.Until(() => Phase(amp) == LooperPhase.PunchIn);
    }

    [Fact]
    public void The_amps_time_signature_does_not_replace_the_owners_choice()
    {
        var amps = new LooperTimeSignature(120, 4, 4, false, true, false, 60);
        var chosen = Looper.Initial.Reported(amps).Chose(new LooperTimeSignature(90, 4, 2, false, false, false, 60));
        Assert.Equal(90u, chosen.Reported(amps).Reported(LooperAction.ResetToReady).Reported(amps).Timing!.Bpm);
    }

    [Fact]
    public void The_settings_page_keeps_a_free_length_as_the_bars_that_fit_in_the_longest_recording()
    {
        Assert.Equal(new LooperTimeSignature(120, 4, 8, false, true, false, 60), Looper.TimingFor(120, click: true, bars: 8, beats: 4, maxSeconds: 60));
        // 120 BPM in 4: a bar is 2 s, so 30 bars fit in 60 s. The click off keeps the length for when it comes back on.
        Assert.Equal(new LooperTimeSignature(120, 4, 30, true, true, false, 60), Looper.TimingFor(120, click: true, bars: 0, beats: 4, maxSeconds: 60));
        Assert.Equal(new LooperTimeSignature(90, 3, 4, false, false, false, 60), Looper.TimingFor(90, click: false, bars: 4, beats: 3, maxSeconds: 60));
        Assert.Equal((40u, 64u), (Looper.MinBpm(4, 4, 60), Looper.MinBpm(16, 4, 60)));
    }

    private static LooperPhase Phase(AmpController amp) => amp.State!.Looper.Phase;

    private async Task Report(AmpController amp, LooperAction action, LooperPhase expected)
    {
        Send(action);
        await Wait.Until(() => Phase(amp) == expected);
    }

    private void Send(LooperAction action) => _link!.Send(new Chunk(_seq++, 0x03, 0x75, new UIntValue((uint)action).Encode()));

    private async Task<AmpController> StartAsync()
    {
        _controller = new AmpController(() => _link = new FakeLink { Respond = _amp.Respond }, new PresetLibrary(_root));
        _controller.Start();
        var controller = _controller;
        await Wait.Until(() => controller is { Link: LinkState.Ready, State: not null });
        return controller;
    }
}
