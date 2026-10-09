using System.Globalization;
using System.Runtime.InteropServices.WindowsRuntime;
using Flint.Core.Connection;
using Flint.Core.Logging;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Radios;

namespace Flint.Bluetooth.WinRT;

/// <summary>
/// The amp over Windows' own Bluetooth LE stack, as <see cref="BlueZ.BlueZLink"/> does it on Linux: service FFC0,
/// writes to FFC1 without response in 20-byte slices, notifications from FFC2. Connects by address; with none (the
/// first run) it scans for the first device whose name contains "Spark 2", skipping the amp's classic audio side,
/// "Spark 2 Audio". The address is written as BlueZ writes it (AA:BB:CC:DD:EE:FF).
/// </summary>
public sealed class WinRtLink(string? address) : ILink
{
    private static readonly Guid ServiceUuid = Uuid(0xffc0), WriteUuid = Uuid(0xffc1), NotifyUuid = Uuid(0xffc2);
    private const int SliceLength = 20;
    private static readonly TimeSpan ScanDuration = TimeSpan.FromSeconds(10);

    private BluetoothLEDevice? _device;
    private GattDeviceService? _service;
    private GattCharacteristic? _write, _notify;
    private int _dropped;

    public event Action<byte[]>? Received;
    public event Action<string>? Dropped;

    /// <summary>The amp's address once found by <see cref="ConnectAsync"/>, so the first run can remember the amp it found by name.</summary>
    public string? Address { get; private set; }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        var adapter = await BluetoothAdapter.GetDefaultAsync().AsTask(cancellationToken)
                      ?? throw new IOException("no Bluetooth adapter");
        if (!adapter.IsLowEnergySupported) throw new IOException("this Bluetooth adapter has no Bluetooth LE");
        if ((await adapter.GetRadioAsync().AsTask(cancellationToken)).State != RadioState.On) throw new IOException("Bluetooth is off");

        var found = address is not null ? Parse(address) : await ScanAsync(cancellationToken);
        Address = Format(found);
        _device = await BluetoothLEDevice.FromBluetoothAddressAsync(found).AsTask(cancellationToken)
                  ?? throw new IOException($"{Address} not found: is the amp on, and is the phone connected to it?");

        // Asking for the service is what connects; uncached, so a stale cache cannot hide it.
        var services = await _device.GetGattServicesForUuidAsync(ServiceUuid, BluetoothCacheMode.Uncached).AsTask(cancellationToken);
        if (services.Status != GattCommunicationStatus.Success || services.Services.Count == 0)
            throw new IOException($"{Address} did not answer ({services.Status}): is the amp on, and is the phone connected to it?");
        _service = services.Services[0];
        _write = await CharacteristicAsync(WriteUuid, cancellationToken);
        _notify = await CharacteristicAsync(NotifyUuid, cancellationToken);

        _notify.ValueChanged += OnValueChanged;
        var status = await _notify.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify)
            .AsTask(cancellationToken);
        if (status != GattCommunicationStatus.Success) throw new IOException($"the amp refused notifications ({status})");

        _device.ConnectionStatusChanged += OnConnectionStatusChanged;
        if (_device.ConnectionStatus == BluetoothConnectionStatus.Disconnected) throw new IOException($"{Address} disconnected while connecting");
    }

    public async Task WriteAsync(ReadOnlyMemory<byte> block, CancellationToken cancellationToken)
    {
        if (_write is null) throw new InvalidOperationException("not connected");
        for (var offset = 0; offset < block.Length; offset += SliceLength)
        {
            var slice = block.Slice(offset, Math.Min(SliceLength, block.Length - offset)).ToArray();
            // Without response, as the official app writes.
            var result = await _write.WriteValueWithResultAsync(slice.AsBuffer(), GattWriteOption.WriteWithoutResponse).AsTask(cancellationToken);
            if (result.Status != GattCommunicationStatus.Success) throw new IOException($"write failed: {result.Status}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _dropped, 1); // our own disconnect is not a drop
        if (_device is not null) _device.ConnectionStatusChanged -= OnConnectionStatusChanged;
        if (_notify is not null)
        {
            _notify.ValueChanged -= OnValueChanged;
            try
            {
                await _notify.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.None)
                    .AsTask().Until(TimeSpan.FromSeconds(3));
            }
            catch (Exception e) when (e is TimeoutException or System.Runtime.InteropServices.COMException or ObjectDisposedException)
            {
                Log.Warn("ble", $"disconnect: {e.Message}");
            }
        }
        // Windows drops the connection once nothing holds the device, which releases the amp so the phone can take it.
        _service?.Dispose();
        _device?.Dispose();
    }

    /// <summary>The first "Spark 2" that advertises within <see cref="ScanDuration"/>.</summary>
    private static async Task<ulong> ScanAsync(CancellationToken cancellationToken)
    {
        Log.Info("ble", $"no amp known yet, scanning {ScanDuration.TotalSeconds:F0} s");
        var found = new TaskCompletionSource<ulong>(TaskCreationOptions.RunContinuationsAsynchronously);
        // Active, so the scan response, which carries the name, is asked for too.
        var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
        watcher.Received += (_, e) =>
        {
            var name = e.Advertisement.LocalName ?? "";
            if (name.Contains("Spark 2", StringComparison.OrdinalIgnoreCase) && !name.EndsWith("Audio", StringComparison.OrdinalIgnoreCase))
                found.TrySetResult(e.BluetoothAddress);
        };
        watcher.Stopped += (_, e) =>
        {
            if (e.Error != BluetoothError.Success) found.TrySetException(new IOException($"Bluetooth LE scan stopped: {e.Error}"));
        };
        watcher.Start();
        try
        {
            return await found.Task.WaitAsync(ScanDuration, cancellationToken);
        }
        catch (TimeoutException)
        {
            throw new IOException("No Spark 2 found: is the amp on, and is the phone connected to it?");
        }
        finally
        {
            watcher.Stop();
        }
    }

    private async Task<GattCharacteristic> CharacteristicAsync(Guid uuid, CancellationToken cancellationToken)
    {
        var result = await _service!.GetCharacteristicsForUuidAsync(uuid, BluetoothCacheMode.Uncached).AsTask(cancellationToken);
        return result.Status == GattCommunicationStatus.Success && result.Characteristics.Count > 0
            ? result.Characteristics[0]
            : throw new IOException($"the amp has no characteristic {uuid.ToString()[4..8]} ({result.Status})");
    }

    private void OnValueChanged(GattCharacteristic sender, GattValueChangedEventArgs e) => Received?.Invoke(e.CharacteristicValue.ToArray());

    private void OnConnectionStatusChanged(BluetoothLEDevice sender, object e)
    {
        if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected) Drop("Windows reports it disconnected");
    }

    private void Drop(string reason)
    {
        if (Interlocked.Exchange(ref _dropped, 1) == 0) Dropped?.Invoke(reason);
    }

    private static Guid Uuid(ushort shortId) => new($"0000{shortId:x4}-0000-1000-8000-00805f9b34fb");

    private static ulong Parse(string text) => ulong.Parse(text.Replace(":", ""), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    private static string Format(ulong value) => string.Join(":", Enumerable.Range(0, 6).Select(i => ((value >> (40 - 8 * i)) & 0xff).ToString("X2")));
}
