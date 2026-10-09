using Flint.Core.Connection;
using Flint.Core.Library;
using Flint.Core.Model;
using Flint.Core.Presets;
using Flint.Core.Protocol;
using Flint.Core.Protocol.Payloads;

namespace Flint.Core.Tests;

// The window's view of the amp over the whole stack: FakeAmp behind fresh FakeLinks, a real supervisor and library.
public sealed class AmpControllerTests : IAsyncDisposable
{
    private static readonly TimeSpan Ms = TimeSpan.FromMilliseconds(1);
    private static readonly SupervisorTimings Fast = new(1000 * Ms, 200 * Ms, 1000 * Ms, 3, [10 * Ms]);

    private readonly string _root = Directory.CreateTempSubdirectory("flint-amp-").FullName;
    private readonly FakeAmp _amp = new();
    private readonly List<FakeLink> _links = [];
    private AmpController? _controller;

    public async ValueTask DisposeAsync()
    {
        if (_controller is not null) await _controller.DisposeAsync();
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task It_syncs_on_connect_and_follows_the_amps_preset_selector_and_our_own()
    {
        _amp.Live = _amp.Slots[2];
        var controller = await StartAsync();
        Assert.Equal(2, controller.State!.CurrentSlot);
        Assert.Equal(_amp.Slots.Select(p => p.Name), controller.State.Slots.Select(p => p.Name));
        Assert.Null(controller.Pending); // the first connect imports, it does not ask

        Link.Send(new Chunk(0x40, 0x03, 0x38, new SwitchPreset(PresetType.Slot, 6).Encode()));
        await Wait.Until(() => controller.State.CurrentSlot == 6);

        await controller.SelectSlotAsync(1);
        Assert.Equal([1], _amp.Selected);
        Assert.Equal((1, "Preset 1"), (controller.State.CurrentSlot, controller.State.Live.Name));
    }

    [Fact]
    public async Task Our_knobs_and_switches_reach_the_amp_and_the_live_preset()
    {
        _amp.Live = _amp.Slots[0];
        var controller = await StartAsync();

        controller.SetParameter("GK800", 4, 0.75f);
        await controller.SetModuleOnAsync("DelayRe201", true);
        await Wait.Until(() => Link.Written.Any(c => c.Op == Opcode.SetParameter));

        Assert.Equal(new DspParam("GK800", 4, 0.75f), DspParam.Decode(Link.Written.Single(c => c.Op == Opcode.SetParameter).Payload));
        Assert.Equal(new DspOnOff("DelayRe201", true), DspOnOff.Decode(Link.Written.Single(c => c.Op == Opcode.SetModuleOn).Payload));
        var live = controller.State!.Live;
        Assert.Equal(0.75f, live.Modules.Single(m => m.DspId == "GK800").Parameters.Single(p => p.Index == 4).Values[0]);
        Assert.True(live.Modules.Single(m => m.DspId == "DelayRe201").Active);
        Assert.True(controller.State.Edited);
    }

    [Fact]
    public async Task A_model_swap_and_a_reverb_type_go_out_as_the_official_app_sends_them()
    {
        _amp.Live = _amp.Slots[0];
        var controller = await StartAsync();
        var before = Link.Written.Count;

        // The state changes as soon as the swap is acked, before the read-back: the panel switches at once.
        string? liveWhenReadStarted = null;
        var respond = Link.Respond;
        Link.Respond = w =>
        {
            if (w.Op == Opcode.DownloadPreset) liveWhenReadStarted = string.Join(",", controller.State!.Live.Modules.Select(m => m.DspId));
            return respond(w);
        };
        var starting = new PresetModule("Sunny3000", true, [new PresetParameter(0, [0.7f])]);

        await controller.ReplaceModelAsync("GK800", "Sunny3000", starting);
        await controller.SetReverbTypeAsync(0.4f);

        Assert.Contains("Sunny3000", liveWhenReadStarted);

        var sent = Link.Written.Skip(before).ToList();
        Assert.Equal([Opcode.ReplaceModule, Opcode.SetModuleOn, Opcode.DownloadPreset, Opcode.SetParameter, Opcode.SetModuleOn],
            sent.Select(c => c.Op));
        Assert.Equal(new DspReplace("GK800", "Sunny3000"), DspReplace.Decode(sent[0].Payload));
        Assert.Equal(new DspOnOff("Sunny3000", true), DspOnOff.Decode(sent[1].Payload));
        Assert.Equal(new DspParam("bias.reverb", 6, 0.4f), DspParam.Decode(sent[3].Payload));
        Assert.Equal(new DspOnOff("bias.reverb", true), DspOnOff.Decode(sent[4].Payload));
        var live = controller.State!.Live;
        Assert.Contains(live.Modules, m => m.DspId == "Sunny3000");
        Assert.DoesNotContain(live.Modules, m => m.DspId == "GK800");
        Assert.Equal(0.4f, live.Modules.Single(m => m.DspId == "bias.reverb").Parameters.Single(p => p.Index == 6).Values[0]);
        Assert.True(controller.State.Edited);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_drop_with_unsaved_edits_warns_after_the_reconnect_only_if_the_amp_lost_them(bool ampKeptThem)
    {
        _amp.Live = _amp.Slots[0];
        var controller = await StartAsync();
        Link.Send(new Chunk(0x40, 0x03, 0x37, new DspParam("GK800", 0, 0.9f).Encode()));
        await Wait.Until(() => controller.State!.Edited);

        _amp.Edited = ampKeptThem;
        Link.Drop("test");
        await Wait.Until(() => _links.Count == 2 && controller.Link == LinkState.Ready);

        Assert.Equal(ampKeptThem ? null : "Connection lost: unsaved changes to \"Preset 0\" were discarded", controller.Warning);
    }

    [Fact]
    public async Task Restoring_after_a_factory_reset_uploads_mine_and_learns_the_factory_presets()
    {
        await (await StartAsync()).StopAsync(); // the first connect fills the library
        var mine = _amp.Slots.Select(p => p.Name).ToList();
        for (var i = 0; i < 8; i++) _amp.Slots[i] = FakeAmp.Sample($"Factory {i}");

        var controller = await StartAsync();
        Assert.True(controller.Pending!.FactoryReset);

        await controller.ResolveAsync(Enumerable.Range(0, 8), restoreMine: true);

        Assert.Null(controller.Pending);
        Assert.Equal(8, _amp.Uploaded.Count);
        Assert.Equal(mine, _amp.Slots.Select(p => p.Name));
        Assert.Equal(mine, controller.State!.Slots.Select(p => p.Name));
        Assert.Equal(8, new PresetLibrary(_root).LoadSlotMap().FactoryIds.Count);
    }

    [Fact]
    public async Task Overwrite_keeps_the_id_saves_the_file_with_history_and_uploads_to_its_slot()
    {
        _amp.Live = _amp.Slots[2];
        var controller = await StartAsync();
        var id = controller.State!.Live.Id;
        controller.SetParameter("GK800", 4, 0.75f);

        await controller.OverwriteAsync();

        Assert.Equal([2], _amp.Uploaded);
        Assert.Equal([2], _amp.Selected);
        Assert.Equal((id, 0.75f), (_amp.Slots[2].Id, Knob(_amp.Slots[2])));
        var library = new PresetLibrary(_root);
        Assert.Equal(0.75f, Knob(library.Load(library.Find(id)!)));
        Assert.Single(Directory.GetDirectories(library.HistoryDirectory));
        Assert.False(controller.State.Edited);

        _amp.Downloaded.Clear();
        controller = await StartAsync(); // the slot map knows the new checksum: nothing to download, nothing to ask
        Assert.Empty(_amp.Downloaded);
        Assert.Null(controller.Pending);
    }

    [Fact]
    public async Task Save_to_hardware_makes_a_new_preset_in_the_library_and_the_slot_and_selects_it()
    {
        _amp.Live = _amp.Slots[0];
        var controller = await StartAsync();
        var old = controller.State!.Live.Id;

        await controller.SaveToSlotAsync("  My Lead ", 5);

        Assert.Equal([5], _amp.Uploaded);
        Assert.Equal([5], _amp.Selected);
        Assert.Equal("My Lead", _amp.Slots[5].Name);
        Assert.NotEqual(old, _amp.Slots[5].Id);
        Assert.Equal((5, "My Lead", false), (controller.State.CurrentSlot, controller.State.Live.Name, controller.State.Edited));
        Assert.Equal("My Lead", new PresetLibrary(_root).Load(new PresetLibrary(_root).LoadSlotMap().Slots[5]!.Preset).Name);
        await Assert.ThrowsAsync<ArgumentException>(() => controller.SaveToSlotAsync(" ", 1));
    }

    [Fact]
    public async Task Save_as_new_adds_a_library_entry_and_leaves_the_amp_alone()
    {
        _amp.Live = _amp.Slots[0];
        var controller = await StartAsync();
        var writes = Link.Written.Count;

        var entry = controller.SaveAsNew("Practice");

        Assert.Equal(writes, Link.Written.Count);
        Assert.Equal((null, "Practice", false), (controller.State!.CurrentSlot, controller.State.Live.Name, controller.State.Edited));
        Assert.Equal(controller.State.Live.Id, new PresetLibrary(_root).Load(entry).Id);
        Assert.NotEqual(_amp.Slots[0].Id, controller.State.Live.Id);
    }

    [Fact]
    public async Task Loading_a_library_preset_plays_it_from_the_temporary_slot_and_stores_nothing()
    {
        _amp.Live = _amp.Slots[0];
        var controller = await StartAsync();
        var practice = controller.State!.Live.AsNew("Practice");

        await controller.LoadAsync(practice);

        Assert.Equal([127], _amp.Uploaded);
        Assert.Equal([127], _amp.Selected);
        Assert.Equal("Practice", _amp.Live!.Name);
        Assert.Equal((null, "Practice"), (controller.State.CurrentSlot, controller.State.Live.Name));
        Link.Send(new Chunk(0x40, 0x03, 0x38, new SwitchPreset(PresetType.Slot, 127).Encode())); // the amp may echo the switch
        await Task.Delay(50);
        Assert.Equal((null, "Practice"), (controller.State.CurrentSlot, controller.State.Live.Name));
    }

    [Fact]
    public async Task Renaming_a_slots_preset_keeps_its_id_and_uploads_it_to_that_slot_again()
    {
        _amp.Live = _amp.Slots[3];
        var controller = await StartAsync();
        var id = _amp.Slots[3].Id;

        await controller.RenameAsync(controller.EntryOfSlot(3)!, "Clean");

        Assert.Equal([3], _amp.Uploaded);
        Assert.Empty(_amp.Selected);
        Assert.Equal(("Clean", id), (_amp.Slots[3].Name, _amp.Slots[3].Id));
        Assert.Equal(("Clean", "Clean"), (controller.State!.Slots[3].Name, controller.State.Live.Name));
        _amp.Downloaded.Clear();
        controller = await StartAsync();
        Assert.Empty(_amp.Downloaded);
        Assert.Null(controller.Pending);
    }

    [Fact]
    public async Task The_device_page_writes_power_settings_line_out_mute_and_the_name_and_reads_them_at_connect()
    {
        _amp.LineOutMuted = true;
        var controller = await StartAsync();
        Assert.True(controller.State!.LineOutMuted);
        Assert.Equal(15u, controller.State.Settings.AutoStandby);

        await controller.SetSettingsAsync(controller.State.Settings with { AutoStandby = 30, AutoShutdown = 0 });
        await controller.SetLineOutMutedAsync(false);
        await controller.RenameAmpAsync("  Den  ");

        // The packer's order on the wire: voice guide, shutdown, jack mode, standby.
        Assert.Equal("c200001e", Convert.ToHexStringLower(Link.Written.Single(c => c.Op == Opcode.SetSettings).Payload));
        Assert.Equal((new SettingInfo(false, 0, 0, 30), false, "Den"), (_amp.Settings, _amp.LineOutMuted, _amp.Name));
        Assert.Equal((30u, false, "Den"), (controller.State.Settings.AutoStandby, controller.State.LineOutMuted, controller.State.Name));
        await Assert.ThrowsAsync<ArgumentException>(() => controller.RenameAmpAsync(new string('x', 41)));
    }

    [Fact]
    public async Task The_tuner_follows_the_amps_button_and_our_switch_and_keeps_the_last_reading_while_on()
    {
        var controller = await StartAsync();
        Link.Send(new Chunk(0x40, 0x03, 0x64, new TunerReading(4, 0.5f).Encode())); // a stray reading while off is ignored
        Link.Send(new Chunk(0x41, 0x03, 0x65, new BoolValue(true).Encode())); // the amp's tuner button
        Link.Send(new Chunk(0x42, 0x03, 0x64, new TunerReading(9, 0.47f).Encode()));
        await Wait.Until(() => controller.State!.Tuning is not null);
        Assert.True(controller.State!.TunerOn);
        Assert.Equal(("A", true), (controller.State.Tuning!.Name, controller.State.Tuning.InTune));

        await controller.SetTunerAsync(false);
        Assert.False(_amp.TunerOn);
        Assert.Equal((false, null), (controller.State.TunerOn, controller.State.Tuning));
        Assert.False(new TunerReading(0, -1f).HasSignal);
    }

    [Fact]
    public async Task An_amp_in_tuner_mode_is_read_with_its_tuner_off_and_connects_with_it_on_again()
    {
        _amp.TunerOn = true; // in tuner mode the amp drops replies (the fake: the firmware and name reads)
        var controller = await StartAsync();
        Assert.True(_amp.TunerOn);
        Assert.Equal((true, "Spark 2", "2.7.2.200"), (controller.State!.TunerOn, controller.State.Name, controller.State.Firmware));
        Assert.Equal([false, true], Link.Written.Where(c => c.Op == Opcode.SetTuner).Select(c => BoolValue.Decode(c.Payload).Value));

        // Quitting with the tuner on switches it off, so the amp is not left in tuner mode.
        await controller.StopAsync();
        Assert.False(_amp.TunerOn);
    }

    private static float Knob(Preset preset) => preset.Modules.Single(m => m.DspId == "GK800").Parameters.Single(p => p.Index == 4).Values[0];

    private FakeLink Link => _links[^1];

    private async Task<AmpController> StartAsync()
    {
        if (_controller is not null) await _controller.DisposeAsync();
        _controller = new AmpController(() =>
        {
            var link = new FakeLink { Respond = _amp.Respond };
            lock (_links) _links.Add(link);
            return link;
        }, new PresetLibrary(_root), Fast);
        _controller.Start();
        var controller = _controller;
        await Wait.Until(() => controller is { Link: LinkState.Ready, State: not null });
        return controller;
    }
}
