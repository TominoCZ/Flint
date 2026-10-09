using Flint.Core.Connection;
using Flint.Core.Logging;
using Tmds.DBus.Protocol;

namespace Flint.Bluetooth.BlueZ;

/// <summary>
/// The amp over BlueZ on Linux: service FFC0, writes to FFC1 without response in 20-byte slices, notifications from
/// FFC2. Connects by address and scans first only when BlueZ has forgotten the device, which it does
/// for unpaired devices some time after the last scan. With no address (the first run) it takes the first
/// device whose name contains "Spark 2", as the official app does, skipping the amp's classic audio side, "Spark 2 Audio".
/// </summary>
public sealed class BlueZLink(string? address) : ILink
{
    private const string WriteUuid = "0000ffc1-0000-1000-8000-00805f9b34fb", NotifyUuid = "0000ffc2-0000-1000-8000-00805f9b34fb";
    private const int SliceLength = 20;
    private static readonly TimeSpan ScanDuration = TimeSpan.FromSeconds(10), Poll = TimeSpan.FromMilliseconds(200);

    private readonly List<IDisposable> _watches = [];
    private BlueZBus? _bus;
    private string? _device, _write;
    private int _dropped;

    public event Action<byte[]>? Received;
    public event Action<string>? Dropped;

    /// <summary>The amp's address once found by <see cref="ConnectAsync"/>, so the first run can remember the amp it found by name.</summary>
    public string? Address { get; private set; }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        _bus = await BlueZBus.ConnectAsync().Until(cancellationToken);
        var objects = await _bus.GetManagedObjectsAsync().Until(cancellationToken);
        var adapter = objects.FirstOrDefault(o => o.Value.ContainsKey(BlueZBus.Adapter)).Key
                      ?? throw new IOException("no Bluetooth adapter");
        if (!objects[adapter][BlueZBus.Adapter]["Powered"].GetBool()) throw new IOException("Bluetooth is off");

        _device = FindDevice(objects);
        if (_device is null)
        {
            Log.Info("ble", $"{(address is null ? "no Spark 2" : $"{address} not")} known to BlueZ, scanning {ScanDuration.TotalSeconds:F0} s");
            _device = await BlueZDiscovery.RunAsync(_bus, adapter, ScanDuration, FindDevice, cancellationToken)
                      ?? throw new IOException($"{address ?? "No Spark 2"} not found: is the amp on, and is the phone connected to it?");
            objects = await _bus.GetManagedObjectsAsync().Until(cancellationToken);
        }
        Address = objects[_device][BlueZBus.Device]["Address"].GetString();

        _watches.Add(await _bus.WatchPropertiesAsync(_device, BlueZBus.Device, changed =>
        {
            if (changed is null) Drop("lost the D-Bus connection to BlueZ");
            else if (changed.TryGetValue("Connected", out var c) && !c.GetBool()) Drop("BlueZ reports it disconnected");
        }));

        try
        {
            await _bus.CallAsync(_device, BlueZBus.Device, "Connect").Until(cancellationToken);
        }
        catch (DBusErrorReplyException e) when (e.ErrorName == "org.bluez.Error.AlreadyConnected")
        {
        }
        while (!(await _bus.GetPropertyAsync(_device, BlueZBus.Device, "ServicesResolved").Until(cancellationToken)).GetBool())
            await Task.Delay(Poll, cancellationToken);

        objects = await _bus.GetManagedObjectsAsync().Until(cancellationToken);
        _write = FindCharacteristic(objects, WriteUuid);
        var notify = FindCharacteristic(objects, NotifyUuid);
        _watches.Add(await _bus.WatchValueAsync(notify, value => Received?.Invoke(value)));
        await _bus.CallAsync(notify, BlueZBus.Characteristic, "StartNotify").Until(cancellationToken);
    }

    public async Task WriteAsync(ReadOnlyMemory<byte> block, CancellationToken cancellationToken)
    {
        if (_bus is null || _write is null) throw new InvalidOperationException("not connected");
        for (var offset = 0; offset < block.Length; offset += SliceLength)
        {
            var slice = block.Slice(offset, Math.Min(SliceLength, block.Length - offset)).ToArray();
            try
            {
                await _bus.CallAsync(_write, BlueZBus.Characteristic, "WriteValue", "aya{sv}", (ref MessageWriter w) =>
                {
                    w.WriteArray(slice);
                    BlueZBus.WriteOptions(ref w, "type", "command"); // without response, as the official app writes
                }).Until(cancellationToken);
            }
            catch (DBusExceptionBase e)
            {
                throw new IOException($"write failed: {e.Message}", e);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _dropped, 1); // our own disconnect is not a drop
        foreach (var watch in _watches) watch.Dispose();
        if (_bus is null) return;
        if (_device is not null)
        {
            try
            {
                // Releases the amp so the phone can take it.
                await _bus.CallAsync(_device, BlueZBus.Device, "Disconnect").Until(TimeSpan.FromSeconds(3));
            }
            catch (Exception e) when (e is DBusExceptionBase or TimeoutException)
            {
                Log.Warn("ble", $"disconnect: {e.Message}");
            }
        }
        _bus.Dispose();
    }

    private void Drop(string reason)
    {
        if (Interlocked.Exchange(ref _dropped, 1) == 0) Dropped?.Invoke(reason);
    }

    private string? FindDevice(ManagedObjects objects) =>
        objects.FirstOrDefault(o => o.Value.TryGetValue(BlueZBus.Device, out var d) && IsAmp(d)).Key;

    private bool IsAmp(Dictionary<string, VariantValue> device)
    {
        if (address is not null)
            return device.TryGetValue("Address", out var a) && string.Equals(a.GetString(), address, StringComparison.OrdinalIgnoreCase);
        var name = device.TryGetValue("Name", out var n) ? n.GetString() : "";
        return name.Contains("Spark 2", StringComparison.OrdinalIgnoreCase) && !name.EndsWith("Audio", StringComparison.OrdinalIgnoreCase);
    }

    private string FindCharacteristic(ManagedObjects objects, string uuid) =>
        objects.FirstOrDefault(o => o.Key.StartsWith(_device + "/")
                                    && o.Value.TryGetValue(BlueZBus.Characteristic, out var c) && c["UUID"].GetString() == uuid).Key
        ?? throw new IOException($"the amp has no characteristic {uuid[4..8]}: stale BlueZ cache?");
}
