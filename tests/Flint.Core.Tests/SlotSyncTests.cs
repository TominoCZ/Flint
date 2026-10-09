using Flint.Core.Library;
using Flint.Core.Presets;
using Flint.Core.Session;

namespace Flint.Core.Tests;

// Slot sync on a FakeAmp and a library in a temp folder: what is downloaded, what is asked, and that no answer loses a file.
public sealed class SlotSyncTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("flint-lib-").FullName;
    private readonly FakeAmp _amp = new();
    private readonly PresetLibrary _library;

    public SlotSyncTests() => _library = new PresetLibrary(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task The_first_connect_imports_all_8_slots_and_the_next_downloads_nothing()
    {
        var first = await SyncAsync();
        Assert.Empty(first.Conflicts);
        Assert.Equal([0, 1, 2, 3, 4, 5, 6, 7], _amp.Downloaded);
        Assert.Equal(8, Directory.GetFiles(_library.PresetsDirectory, "*.json").Length);

        _amp.Downloaded.Clear();
        var second = await SyncAsync();
        Assert.Empty(_amp.Downloaded);
        Assert.Empty(second.Conflicts);
        Assert.Equal(_amp.Slots.Select(p => p.Name), second.Slots.Select(p => p.Name));
    }

    [Fact]
    public async Task A_slot_the_phone_changed_is_asked_about_and_keeping_the_amps_adds_a_file()
    {
        await SyncAsync();
        var mine = _amp.Slots[2];
        _amp.Slots[2] = FakeAmp.Sample("From the phone");
        _amp.Downloaded.Clear();

        var result = await SyncAsync();
        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal([2], _amp.Downloaded);
        Assert.False(result.FactoryReset);
        Assert.Equal((2, mine.Name, "From the phone"), (conflict.Slot, conflict.Mine.Name, conflict.Amps.Name));

        SlotSync.KeepAmps(_library, conflict);
        Assert.Equal(9, Directory.GetFiles(_library.PresetsDirectory, "*.json").Length); // the old one stays
        Assert.Empty((await SyncAsync()).Conflicts);
    }

    [Fact]
    public async Task Restoring_mine_uploads_it_and_the_checksum_recorded_is_what_the_amp_then_reports()
    {
        await SyncAsync();
        var mine = _amp.Slots[5];
        _amp.Slots[5] = FakeAmp.Sample("From the phone");

        var conflict = Assert.Single((await SyncAsync()).Conflicts);
        await WithSession(s => SlotSync.RestoreAsync(s, _library, conflict, default));

        Assert.Equal([5], _amp.Uploaded);
        Assert.Equal(mine.Id, _amp.Slots[5].Id);
        Assert.Empty((await SyncAsync()).Conflicts);
    }

    [Fact]
    public async Task Every_slot_replaced_looks_like_a_factory_reset_and_once_learned_one_factory_preset_does()
    {
        await SyncAsync();
        for (var i = 0; i < 8; i++) _amp.Slots[i] = FakeAmp.Sample($"Factory {i}");

        var reset = await SyncAsync();
        Assert.True(reset.FactoryReset);
        Assert.Equal(8, reset.Conflicts.Count);
        SlotSync.RememberFactoryPresets(_library, reset.Conflicts.Select(c => c.Amps));
        foreach (var conflict in reset.Conflicts.Skip(1)) await WithSession(s => SlotSync.RestoreAsync(s, _library, conflict, default));

        var one = await SyncAsync(); // slot 0 still holds a factory preset
        Assert.Equal(0, Assert.Single(one.Conflicts).Slot);
        Assert.True(one.FactoryReset);
    }

    [Fact]
    public void Presets_with_the_same_name_get_their_own_files()
    {
        var a = FakeAmp.Sample("Clean: Rich?");
        Assert.Equal("Clean_ Rich_", _library.Add(a));
        Assert.Equal("Clean_ Rich_ (2)", _library.Add(a with { Bpm = 90 }));
        Assert.Equal(120f, _library.Load("Clean_ Rich_").Bpm);
    }

    [Fact]
    public void An_unreadable_slot_map_is_set_aside_not_overwritten()
    {
        File.WriteAllText(Path.Combine(_root, "slots.json"), "{ not json");

        Assert.Equal(SlotMap.Empty.Slots, _library.LoadSlotMap().Slots);
        Assert.Single(Directory.GetFiles(_root, "slots.json.bad-*"));
    }

    private Task<SyncResult> SyncAsync() =>
        WithSession(s => SlotSync.RunAsync(s, _library, [.. _amp.Slots.Select(FakeAmp.ChecksumOf)], default));

    private async Task<T> WithSession<T>(Func<AmpSession, Task<T>> run)
    {
        var link = new FakeLink { Respond = _amp.Respond };
        await link.ConnectAsync(default);
        using var session = new AmpSession(link);
        return await run(session);
    }

    private Task WithSession(Func<AmpSession, Task> run) => WithSession(async s =>
    {
        await run(s);
        return 0;
    });
}
