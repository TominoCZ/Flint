using Flint.Core.Connection;
using Flint.Core.Library;
using Flint.Core.Logging;
using Flint.Core.Presets;
using Flint.Core.Protocol;
using Flint.Core.Protocol.Payloads;
using Flint.Core.Session;

namespace Flint.Core.Model;

/// <summary>
/// The amp as the window sees it. Keeps it connected (<see cref="Supervisor"/>), reads it and syncs its slots with the
/// library on every connect, keeps <see cref="State"/> current from the amp's notifies, and carries the owner's slot
/// actions to the amp. <see cref="Changed"/> is raised on any thread; UI code posts it to the UI thread.
/// </summary>
public sealed class AmpController : IAsyncDisposable
{
    private const string ReverbModule = "bias.reverb";

    /// <summary>The app's limit for the amp's name (NameEditFragment's default).</summary>
    public const int MaxAmpName = 40;

    private readonly PresetLibrary _library;
    private readonly Supervisor _supervisor;
    private readonly Lock _gate = new();
    private LinkState _link;
    private string? _editedAtDrop;

    public AmpController(Func<ILink> newLink, PresetLibrary library, SupervisorTimings? timings = null)
    {
        _library = library;
        _supervisor = new Supervisor(newLink, SyncAsync, timings);
        _supervisor.StateChanged += OnLinkState;
    }

    public LinkState Link => _supervisor.State;

    /// <summary>Why the last connection attempt ended, for the window's red or amber state.</summary>
    public string? LastError => _supervisor.LastError;

    /// <summary>In <see cref="LinkState.Backoff"/>: how long until the next attempt.</summary>
    public TimeSpan RetryIn => _supervisor.RetryIn;

    /// <summary>The amp as of the last sync and its notifies since; null until the first sync. Kept, stale, while reconnecting.</summary>
    public AmpState? State { get; private set; }

    /// <summary>Slots that differ from the library and wait for the owner's answer (decision 9), or null.</summary>
    public SyncResult? Pending { get; private set; }

    /// <summary>Set after a reconnect if the drop lost unsaved edits (decision 7). The window shows it for a while.</summary>
    public string? Warning { get; private set; }

    public event Action? Changed;

    public void Start() => _supervisor.Start();

    public async Task StopAsync()
    {
        await TunerOffAsync();
        await _supervisor.StopAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await TunerOffAsync();
        await _supervisor.DisposeAsync();
    }

    /// <summary>Letting the amp go with its tuner on would leave it in tuner mode, so it goes off first, briefly tried.</summary>
    private async Task TunerOffAsync()
    {
        if (State is not { TunerOn: true } || Link != LinkState.Ready) return;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            await SetTunerAsync(false, timeout.Token);
        }
        catch (Exception e) when (e is TimeoutException or OperationCanceledException or IOException or InvalidOperationException)
        {
            Log.Warn("amp", $"could not switch the tuner off: {e.Message}");
        }
    }

    public async Task SelectSlotAsync(int slot, CancellationToken ct = default)
    {
        await ReadySession().RequestAsync(Opcode.SelectSlot, new SwitchPreset(PresetType.Slot, slot), ct);
        Update(s => s.WithCurrentSlot(slot));
    }

    /// <summary>
    /// Turns a knob: sent within the session's knob interval, latest value wins, no ack (<c>01 04</c>). The live preset
    /// follows at once and counts as edited, as the official app shows it.
    /// </summary>
    public void SetParameter(string dspId, int index, float value)
    {
        ReadySession().SetParameter(new DspParam(dspId, index, value));
        Update(s => s with { Live = s.Live.WithParameter(dspId, index, value), Edited = true });
    }

    /// <summary>Switches a module on or off (<c>01 15</c>, acked <c>04 15</c>), as swiping it in the strip does.</summary>
    public async Task SetModuleOnAsync(string dspId, bool on, CancellationToken ct = default)
    {
        await ReadySession().RequestAsync(Opcode.SetModuleOn, new DspOnOff(dspId, on), ct);
        Update(s => s with { Live = s.Live.WithModuleOn(dspId, on), Edited = true });
    }

    /// <summary>
    /// Swaps a chain position's model as the official app does: <c>01 06</c> old → new, then <c>01 15</c>
    /// switching the new one on, both acked. The amp loads the model's own starting values, so the live preset is read
    /// back for the panel to show them. That read is most of the wait, so the state changes as soon as the swap is acked,
    /// with <paramref name="starting"/> (the app's own starting values for the model) until the read corrects it.
    /// </summary>
    public async Task ReplaceModelAsync(string fromDspId, string toDspId, PresetModule? starting = null, CancellationToken ct = default)
    {
        var session = ReadySession();
        await session.RequestAsync(Opcode.ReplaceModule, new DspReplace(fromDspId, toDspId), ct);
        Update(s => s with
        {
            Live = s.Live.WithModuleReplaced(fromDspId, (starting ?? s.Live.Modules.First(m => m.DspId == fromDspId)) with { DspId = toDspId, Active = true }),
            Edited = true,
        });
        await session.RequestAsync(Opcode.SetModuleOn, new DspOnOff(toDspId, true), ct);
        var (_, _, live) = await session.DownloadPresetAsync(PresetType.Editing, 0, ct);
        Update(s => s with { Live = live, Edited = true });
    }

    /// <summary>
    /// Picks a reverb: Spark 2's reverbs are one module, <c>bias.reverb</c>, whose param 6 is the type.
    /// As the official app: the value, then <c>01 15</c> switching the reverb on.
    /// </summary>
    public async Task SetReverbTypeAsync(float type, CancellationToken ct = default)
    {
        var session = ReadySession();
        await session.SendAsync(Opcode.SetParameter, new DspParam(ReverbModule, 6, type));
        await session.RequestAsync(Opcode.SetModuleOn, new DspOnOff(ReverbModule, true), ct);
        Update(s => s with { Live = s.Live.WithParameter(ReverbModule, 6, type).WithModuleOn(ReverbModule, true), Edited = true });
    }

    /// <summary>
    /// Answers pending slots: <paramref name="restoreMine"/> uploads the library's presets, otherwise the amp's are kept
    /// as new library entries. Restoring after a suspected factory reset teaches the library those factory presets.
    /// </summary>
    public async Task ResolveAsync(IEnumerable<int> slots, bool restoreMine, CancellationToken ct = default)
    {
        var session = ReadySession();
        var pending = Pending ?? throw new InvalidOperationException("no slots are waiting for an answer");
        var chosen = pending.Conflicts.Where(c => slots.Contains(c.Slot)).ToList();
        foreach (var conflict in chosen)
        {
            if (!restoreMine)
            {
                SlotSync.KeepAmps(_library, conflict);
            }
            else
            {
                await SlotSync.RestoreAsync(session, _library, conflict, ct);
                // As the official app does after a save: reselect, so the amp plays what its slot now holds.
                await StoredAsync(conflict.Slot, conflict.Mine, select: State?.CurrentSlot == conflict.Slot, ct);
            }
            DropPending(conflict.Slot);
        }
        if (restoreMine && pending.FactoryReset) SlotSync.RememberFactoryPresets(_library, chosen.Select(c => c.Amps));
    }

    /// <summary>
    /// The ⋮ menu's Overwrite: the live preset, id and name kept, over its library file (the old one goes
    /// to history), then, if it came from a slot, to that slot, which is reselected. A slot preset needs the amp connected.
    /// </summary>
    public async Task OverwriteAsync(CancellationToken ct = default)
    {
        var state = State ?? throw new InvalidOperationException("nothing to save yet");
        var session = state.CurrentSlot is null ? null : ReadySession();
        var preset = state.Live;
        var entry = EntryOf(state);
        if (entry is null) entry = _library.Add(preset);
        else _library.Save(entry, preset);
        Log.Info("lib", $"saved \"{preset.Name}\" over {entry}");
        if (state.CurrentSlot is { } slot) await PutAsync(session!, slot, entry, preset, ct);
        else Update(s => s with { Edited = false });
    }

    /// <summary>The ⋮ menu's Save as New: the live preset under a new id and name, as a new library entry. The amp is not touched.</summary>
    public string SaveAsNew(string name)
    {
        var preset = (State ?? throw new InvalidOperationException("nothing to save yet")).Live.AsNew(name);
        var entry = _library.Add(preset);
        Log.Info("lib", $"saved \"{preset.Name}\" as {entry}");
        Update(s => s with { Live = preset, CurrentSlot = null, Edited = false });
        return entry;
    }

    /// <summary>The ⋮ menu's Save to hardware preset: the live preset under a new id and name, to the library, then to the slot, which is selected.</summary>
    public async Task SaveToSlotAsync(string name, int slot, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(slot, AmpState.SlotCount);
        var session = ReadySession();
        var preset = (State ?? throw new InvalidOperationException("nothing to save yet")).Live.AsNew(name);
        var entry = _library.Add(preset);
        Log.Info("lib", $"saved \"{preset.Name}\" as {entry} for slot {slot + 1}");
        await PutAsync(session, slot, entry, preset, ct);
    }

    /// <summary>The ⋮ menu's Import presets: one preset file's text into the library (<see cref="PresetLibrary.Import"/>).</summary>
    public string Import(string json)
    {
        var entry = _library.Import(json);
        Log.Info("lib", $"imported {entry}");
        return entry;
    }

    /// <summary>
    /// The ▾ list's library row: plays a library preset without storing it, as the app does, by uploading it to the amp's
    /// temporary slot and switching to that. No stored slot changes.
    /// </summary>
    public async Task LoadAsync(Preset preset, CancellationToken ct = default)
    {
        var session = ReadySession();
        await session.UploadPresetAsync(PresetType.Slot, AmpState.TemporarySlot, preset, ct);
        await session.RequestAsync(Opcode.SelectSlot, new SwitchPreset(PresetType.Slot, AmpState.TemporarySlot), ct);
        Update(s => s with { Live = preset, CurrentSlot = null, Edited = false });
    }

    /// <summary>
    /// The ▾ list's rename: the library file is rewritten under the new name, id kept (the old one goes to history), and
    /// every slot holding that entry gets it uploaded again, as the app does for a hardware preset.
    /// </summary>
    public async Task RenameAsync(string entry, string name, CancellationToken ct = default)
    {
        var preset = _library.Load(entry) with { Name = Preset.ValidName(name) };
        var map = _library.LoadSlotMap();
        var slots = Enumerable.Range(0, AmpState.SlotCount).Where(i => map.Slots[i]?.Preset == entry).ToList();
        var session = slots.Count > 0 ? ReadySession() : null;
        _library.Save(entry, preset);
        Log.Info("lib", $"renamed {entry} to \"{preset.Name}\"");
        foreach (var slot in slots)
        {
            await SlotSync.PutAsync(session!, _library, slot, entry, preset, ct);
            DropPending(slot);
            await StoredAsync(slot, preset, select: false, ct);
        }
        Update(s => s.Live.Id == preset.Id ? s with { Live = s.Live with { Name = preset.Name } } : s);
    }

    /// <summary>The device page's Power Settings (<c>01 72</c>): the app writes all four fields, changed or not.</summary>
    public async Task SetSettingsAsync(SettingInfo settings, CancellationToken ct = default)
    {
        Accepted(await ReadySession().RequestAsync(Opcode.SetSettings, settings, ct));
        Update(s => s with { Settings = settings });
    }

    /// <summary>The device page's Line Out Mute Behavior (<c>01 a2</c>, acked <c>04 a2</c>).</summary>
    public async Task SetLineOutMutedAsync(bool muted, CancellationToken ct = default)
    {
        await ReadySession().RequestAsync(Opcode.SetLineOutMute, new BoolValue(muted), ct);
        Update(s => s with { LineOutMuted = muted });
    }

    /// <summary>The device page's Name (<c>01 11</c>, acked <c>04 11</c>): what the amp calls itself, up to 40 characters as in the app.</summary>
    public async Task RenameAmpAsync(string name, CancellationToken ct = default)
    {
        name = name.Trim();
        if (name.Length is 0 or > MaxAmpName) throw new ArgumentException($"a name has 1 to {MaxAmpName} characters", nameof(name));
        await ReadySession().RequestAsync(Opcode.SetName, new PrefixedText(name), ct);
        Update(s => s with { Name = name });
    }

    /// <summary>Switches the amp's tuner (<c>01 65</c>, acked <c>04 65</c>); readings then arrive as <c>03 64</c> until it is off.</summary>
    public async Task SetTunerAsync(bool on, CancellationToken ct = default)
    {
        var session = ReadySession();
        await session.RequestAsync(Opcode.SetTuner, new BoolValue(on), ct);
        Update(s => s.TunerOn == on ? s : s.WithTuner(on));
    }

    /// <summary>
    /// A Simple Looper button: its actions (<c>01 75</c>, each acked <c>04 75</c>) in order, each applied once acked.
    /// The screen then follows the amp's own reports (<c>03 75</c>).
    /// </summary>
    public async Task PressLooperAsync(LooperButton button, CancellationToken ct = default)
    {
        var session = ReadySession();
        foreach (var action in State?.Looper.ActionsFor(button) ?? [])
        {
            await session.RequestAsync(Opcode.SetLooperAction, new UIntValue((uint)action), ct);
            Update(s => s with { Looper = s.Looper.Sent(button, action) });
        }
    }

    /// <summary>
    /// The looper's timing and what it is doing (<c>02 76</c>, <c>02 75</c>), read as the app does when its looper page
    /// opens. The timing first: Spark 2 is seen to send <c>03 76</c>, while <c>02 75</c> has not been captured yet.
    /// </summary>
    public async Task ReadLooperAsync(CancellationToken ct = default)
    {
        var session = ReadySession();
        var timing = await session.GetAsync<LooperTimeSignature>(Opcode.GetLooperTimeSignature, null, ct);
        Update(s => s with { Looper = s.Looper with { Timing = timing } });
        var action = (LooperAction)(await session.GetAsync<UIntValue>(Opcode.GetLooperAction, null, ct)).Value;
        Update(s => s with { Looper = s.Looper.Reported(action, initial: true) });
    }

    /// <summary>The looper's settings page: tempo, length and count-in (<c>01 76</c>, acked <c>04 76</c>).</summary>
    public async Task SetLooperTimingAsync(LooperTimeSignature timing, CancellationToken ct = default)
    {
        await ReadySession().RequestAsync(Opcode.SetLooperTimeSignature, timing, ct);
        Update(s => s with { Looper = s.Looper with { Timing = timing } });
    }

    /// <summary>The library entry a slot holds, or null while the slot waits for the owner's answer or has none.</summary>
    public string? EntryOfSlot(int slot) =>
        Pending?.Conflicts.Any(c => c.Slot == slot) == true ? null : _library.LoadSlotMap().Slots[slot]?.Preset;

    /// <summary>Every preset in the library, for the ▾ list's All tab.</summary>
    public IReadOnlyList<(string Entry, Preset Preset)> LibraryPresets() => [.. _library.Entries()];

    public void DismissWarning()
    {
        Warning = null;
        Changed?.Invoke();
    }

    /// <summary>
    /// The library entry the live preset was loaded from: its slot's, unless that slot waits for the owner's answer (the
    /// amp then holds a preset the slot map does not), or else whichever file has its id.
    /// </summary>
    private string? EntryOf(AmpState state) =>
        (state.CurrentSlot is { } slot ? EntryOfSlot(slot) : null) ?? _library.Find(state.Live.Id);

    /// <summary>A save reaching a slot: uploaded, recorded, any question about that slot settled, and the slot selected.</summary>
    private async Task PutAsync(AmpSession session, int slot, string entry, Preset preset, CancellationToken ct)
    {
        await SlotSync.PutAsync(session, _library, slot, entry, preset, ct);
        DropPending(slot);
        await StoredAsync(slot, preset, select: true, ct);
    }

    /// <summary>The state after a preset was uploaded to a slot; selecting it then makes it the live, unedited preset.</summary>
    private async Task StoredAsync(int slot, Preset preset, bool select, CancellationToken ct)
    {
        var checksum = PresetTransfer.UploadChecksum(preset);
        Update(s => s with
        {
            Slots = [.. s.Slots.Select((p, i) => i == slot ? preset : p)],
            Checksums = [.. s.Checksums.Select((c, i) => i == slot ? checksum : c)],
        });
        if (select) await SelectSlotAsync(slot, ct);
    }

    private void DropPending(int slot)
    {
        lock (_gate)
        {
            var left = Pending?.Conflicts.Where(c => c.Slot != slot).ToList() ?? [];
            Pending = left.Count > 0 ? Pending! with { Conflicts = left } : null;
        }
        Changed?.Invoke();
    }

    /// <summary>The supervisor's sync step. Notifies that arrive meanwhile are applied once it is done.</summary>
    private async Task SyncAsync(AmpSession session, CancellationToken ct)
    {
        var early = new List<(Chunk, IPayload?)>();
        var synced = false;
        session.Notified += (chunk, payload) =>
        {
            lock (_gate)
            {
                if (!synced)
                {
                    early.Add((chunk, payload));
                    return;
                }
            }
            var followUp = (chunk.Op, payload) is (Opcode.LooperState, UIntValue reported)
                ? State?.Looper.FollowUp((LooperAction)reported.Value) : null;
            Update(s => s.Apply(chunk, payload));
            if (followUp is { } action) _ = FollowUpAsync(session, action);
        };

        var state = await Handshake.ReadAsync(session, ct);
        if (state.TunerOn) await session.RequestAsync(Opcode.SetTuner, new BoolValue(true), ct); // back on, as it was found
        var result = await SlotSync.RunAsync(session, _library, state.Checksums, ct);
        lock (_gate)
        {
            State = early.Aggregate(state.WithSlots(result.Slots), (s, n) => s.Apply(n.Item1, n.Item2));
            Pending = result.Conflicts.Count > 0 ? result : null;
            if (_editedAtDrop is { } name && !State.Edited)
                Warning = $"Connection lost: unsaved changes to \"{name}\" were discarded";
            _editedAtDrop = null;
            synced = true;
        }
        Changed?.Invoke();
    }

    /// <summary>An action the looper sends by itself (a Retry's new count-in), as the app does: off the notify loop, failures logged.</summary>
    private async Task FollowUpAsync(AmpSession session, LooperAction action)
    {
        try
        {
            await session.RequestAsync(Opcode.SetLooperAction, new UIntValue((uint)action));
            Update(s => s with { Looper = s.Looper.Reported(action, byUs: true) });
        }
        catch (Exception e) when (e is TimeoutException or IOException or InvalidOperationException)
        {
            Log.Warn("amp", $"looper {action} failed: {e.Message}");
        }
    }

    private void OnLinkState(LinkState link)
    {
        lock (_gate)
        {
            if (_link == LinkState.Ready && link != LinkState.Ready && State is { Edited: true } edited) _editedAtDrop = edited.Live.Name;
            _link = link;
        }
        Changed?.Invoke();
    }

    private void Update(Func<AmpState, AmpState> change)
    {
        lock (_gate)
        {
            if (State is null) return;
            State = change(State);
        }
        Changed?.Invoke();
    }

    /// <summary>A set the amp answers with a status (<c>04 72</c>): anything but 0 is a refusal, as the app reads it.</summary>
    private static void Accepted(Chunk reply)
    {
        var status = reply.Payload.Length == 0 ? 0 : UIntValue.Decode(reply.Payload).Value;
        if (status != 0) throw new InvalidOperationException($"the amp refused {reply.Op:x4} with status {status}");
    }

    private AmpSession ReadySession() =>
        _supervisor is { State: LinkState.Ready, Session: { } session } ? session : throw new InvalidOperationException("the amp is not connected");
}
