using Flint.Bluetooth.BlueZ;
using Flint.Core.Connection;
using Flint.Core.Presets;
using Flint.Core.Protocol;
using Flint.Core.Protocol.Payloads;
using Flint.Core.Session;

namespace BluezProbe;

/// <summary>The probe's two runs. Neither writes to the amp.</summary>
internal sealed class Probe(string address)
{
    public async Task<int> ReadAsync(TimeSpan listen, CancellationToken stop)
    {
        await using var link = NewLink();
        await link.ConnectAsync(stop);
        using var session = new AmpSession(link);
        var checksums = await HandshakeAsync(session, stop);

        var (_, _, live) = await session.DownloadPresetAsync(PresetType.Editing, 0, stop);
        Say($"edit buffer: \"{live.Name}\"");
        for (var slot = 0; slot < 8; slot++)
        {
            var (_, _, preset) = await session.DownloadPresetAsync(PresetType.Slot, slot, stop);
            var sum = PresetCodec.Checksum(PresetCodec.Pack(preset));
            Say($"slot {slot}: \"{preset.Name}\"  sum {sum:x2}, 02 2b says {checksums[slot]:x2}{(sum == checksums[slot] ? "" : "  MISMATCH")}");
        }

        try // the app's fallback for the current slot: does Spark 2 answer it?
        {
            Say($"02 10 current slot: {Convert.ToHexString((await session.RequestAsync(0x0210, cancellationToken: stop)).Payload)}");
        }
        catch (TimeoutException)
        {
            Say("02 10 current slot: no answer");
        }

        Say($"listening {listen.TotalSeconds:F0} s: turn a knob on the amp");
        await Task.Delay(listen, stop).ContinueWith(_ => { });
        return 0;
    }

    /// <summary>The reconnect check: run, then break the link every way you can, and count the reconnects.</summary>
    public async Task<int> WatchAsync(CancellationToken stop)
    {
        var readies = 0;
        DateTime? lostAt = null;
        await using var supervisor = new Supervisor(NewLink, async (s, ct) => await HandshakeAsync(s, ct));
        supervisor.StateChanged += state =>
        {
            if (state == LinkState.Backoff)
            {
                lostAt ??= DateTime.Now;
                Say($"down: {supervisor.LastError}");
            }
            else if (state == LinkState.Ready)
            {
                Say(readies++ == 0 ? "READY" : $"READY again, reconnect #{readies - 1} after {(DateTime.Now - lostAt!.Value).TotalSeconds:F1} s down");
                lostAt = null;
            }
        };
        supervisor.Start();
        await Task.Delay(Timeout.Infinite, stop).ContinueWith(_ => { });
        Say($"stopped: {Math.Max(0, readies - 1)} reconnects");
        return 0;
    }

    private ILink NewLink() => new TracingLink(new BlueZLink(address));

    /// <summary>The official app's connect reads, minus the serial. Returns the slot checksums.</summary>
    private static async Task<IReadOnlyList<uint>> HandshakeAsync(AmpSession session, CancellationToken ct)
    {
        var states = await session.GetAsync<SlotStates>(Opcode.GetSlotStates, new UIntArray([0]), ct);
        var info = await session.GetAsync<SystemInfo>(Opcode.GetSystemInfo, null, ct);
        var name = await session.GetAsync<PrefixedText>(Opcode.GetName, null, ct);
        var firmware = BitConverter.GetBytes((await session.GetAsync<UIntValue>(Opcode.GetFirmware, null, ct)).Value);
        var checksums = (await session.GetAsync<UIntArray>(Opcode.GetChecksums, new UIntValue(0), ct)).Values;
        var settings = await session.GetAsync<SettingInfo>(Opcode.GetSettings, null, ct);
        Say($"\"{name.Text}\" firmware {firmware[3]}.{firmware[2]}.{firmware[1]}.{firmware[0]}, {info}, " +
            $"slot states {string.Join(' ', states.States)}, {settings}");
        Say($"checksums {string.Join(' ', checksums.Select(c => c.ToString("x2")))}");
        return checksums;
    }

    private static void Say(string line) => Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} ** {line}");
}
