using Tmds.DBus.Protocol;

namespace Flint.Bluetooth.BlueZ;

/// <summary>An LE scan on one adapter that stops as soon as a wanted device shows up in BlueZ's object tree.</summary>
internal static class BlueZDiscovery
{
    // BlueZ forgets the unpaired amp 30 s after it disconnects (TemporaryTimeout), so most starts scan; a tight poll
    // connects as soon as the amp's advert lands. One local D-Bus call each.
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Scans until <paramref name="find"/> returns a value or <paramref name="duration"/> passes (then null). Throws
    /// <see cref="IOException"/> naming the fix when the controller refuses LE scans.
    /// </summary>
    public static async Task<T?> RunAsync<T>(BlueZBus bus, string adapter, TimeSpan duration, Func<ManagedObjects, T?> find,
        CancellationToken cancellationToken) where T : class
    {
        try
        {
            await bus.CallAsync(adapter, BlueZBus.Adapter, "SetDiscoveryFilter", "a{sv}",
                (ref MessageWriter w) => BlueZBus.WriteOptions(ref w, "Transport", "le"));
            await bus.CallAsync(adapter, BlueZBus.Adapter, "StartDiscovery");
        }
        catch (DBusErrorReplyException e)
        {
            // With ControllerMode = bredr the kernel answers LE discovery with mgmt status 0x0b, "Rejected".
            throw new IOException($"Bluetooth LE scan refused ({e.ErrorName}: {e.Message}). If BlueZ runs with LE off, " +
                                  "set ControllerMode = dual in /etc/bluetooth/main.conf and restart bluetooth", e);
        }

        try
        {
            for (var end = DateTime.UtcNow + duration; DateTime.UtcNow < end;)
            {
                await Task.Delay(Poll, cancellationToken);
                if (find(await bus.GetManagedObjectsAsync()) is { } found) return found;
            }
            return null;
        }
        finally
        {
            try
            {
                await bus.CallAsync(adapter, BlueZBus.Adapter, "StopDiscovery");
            }
            catch (DBusExceptionBase)
            {
                // Already stopped, or the adapter went away. Neither matters once we have stopped looking.
            }
        }
    }
}
