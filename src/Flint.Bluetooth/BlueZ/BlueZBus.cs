global using ManagedObjects = System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, Tmds.DBus.Protocol.VariantValue>>>;
using Tmds.DBus.Protocol;

namespace Flint.Bluetooth.BlueZ;

/// <summary>Writes a method call's arguments. Takes the writer by ref: it is a ref struct, and a copy loses what was written.</summary>
internal delegate void WriteArgs(ref MessageWriter writer);

/// <summary>
/// The handful of org.bluez D-Bus calls a BLE central needs, over raw Tmds.DBus.Protocol.
/// Proven in spikes/BluezProbe; <see cref="BlueZLink"/> is built on it.
/// </summary>
internal sealed class BlueZBus : IDisposable
{
    public const string Service = "org.bluez";
    public const string Adapter = "org.bluez.Adapter1", Device = "org.bluez.Device1", Characteristic = "org.bluez.GattCharacteristic1";

    private readonly DBusConnection _bus;

    private BlueZBus(DBusConnection bus) => _bus = bus;

    public static async Task<BlueZBus> ConnectAsync()
    {
        var bus = new DBusConnection(DBusAddress.System!);
        await bus.ConnectAsync();
        return new BlueZBus(bus);
    }

    /// <summary>Every BlueZ object: path → interface → property → value.</summary>
    public Task<ManagedObjects> GetManagedObjectsAsync() =>
        _bus.CallMethodAsync(Build("/", "org.freedesktop.DBus.ObjectManager", "GetManagedObjects"), (Message m, object? _) =>
        {
            var reader = m.GetBodyReader();
            var objects = new ManagedObjects();
            var objectsEnd = reader.ReadDictionaryStart();
            while (reader.HasNext(objectsEnd))
            {
                var interfaces = objects[reader.ReadObjectPathAsString()] = [];
                var interfacesEnd = reader.ReadDictionaryStart();
                while (reader.HasNext(interfacesEnd))
                    interfaces[reader.ReadString()] = reader.ReadDictionaryOfStringToVariantValue();
            }
            return objects;
        }, null);

    public Task CallAsync(string path, string @interface, string member, string? signature = null, WriteArgs? args = null) =>
        _bus.CallMethodAsync(Build(path, @interface, member, signature, args));

    public Task<VariantValue> GetPropertyAsync(string path, string @interface, string property)
    {
        var message = Build(path, "org.freedesktop.DBus.Properties", "Get", "ss", (ref MessageWriter w) =>
        {
            w.WriteString(@interface);
            w.WriteString(property);
        });
        return _bus.CallMethodAsync(message, (Message m, object? _) => m.GetBodyReader().ReadVariantValue(), null);
    }

    /// <summary>
    /// Calls <paramref name="onChanged"/> with the changed properties of one interface on one object, or with null if
    /// the bus connection fails, after which nothing more arrives.
    /// </summary>
    public async Task<IDisposable> WatchPropertiesAsync(string path, string @interface, Action<Dictionary<string, VariantValue>?> onChanged) =>
        await _bus.WatchPropertiesChangedAsync(Service, path, @interface,
            (Message m, object? _) =>
            {
                var reader = m.GetBodyReader();
                reader.ReadString(); // the interface name
                return reader.ReadDictionaryOfStringToVariantValue();
            },
            (Notification<Dictionary<string, VariantValue>> n) =>
            {
                if (n.HasValue) onChanged(n.Value);
                else if (n.Type != NotificationType.ObserverDisposed) onChanged(null);
            }, ObserverFlags.None, emitOnCapturedContext: false, state: null);

    /// <summary>Calls <paramref name="onValue"/> with every new value of a GATT characteristic (its notifications).</summary>
    public Task<IDisposable> WatchValueAsync(string characteristicPath, Action<byte[]> onValue) =>
        WatchPropertiesAsync(characteristicPath, Characteristic, changed =>
        {
            if (changed is not null && changed.TryGetValue("Value", out var v)) onValue(v.GetArray<byte>());
        });

    /// <summary>An a{sv} with one string entry, BlueZ's usual "options" argument.</summary>
    public static void WriteOptions(ref MessageWriter w, string key, string value)
    {
        var start = w.WriteDictionaryStart();
        w.WriteDictionaryEntryStart();
        w.WriteString(key);
        w.WriteVariantString(value);
        w.WriteDictionaryEnd(start);
    }

    public void Dispose() => _bus.Dispose();

    // Built synchronously because MessageWriter, a ref struct, cannot live across an await.
    private MessageBuffer Build(string path, string @interface, string member, string? signature = null, WriteArgs? args = null)
    {
        var w = _bus.GetMessageWriter();
        try
        {
            w.WriteMethodCallHeader(Service, path, @interface, member, signature, MessageFlags.None);
            args?.Invoke(ref w);
            return w.CreateMessage();
        }
        finally
        {
            w.Dispose();
        }
    }
}
