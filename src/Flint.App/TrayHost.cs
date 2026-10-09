using Tmds.DBus.Protocol;

namespace Flint.App;

/// <summary>
/// Whether a tray icon will be seen. Windows always has a tray. Linux shows one only through a StatusNotifier host
/// (KDE, most other desktops; GNOME only with the AppIndicator extension), and Avalonia's tray says nothing when there
/// is none, so the app asks the session bus's StatusNotifierWatcher itself.
/// </summary>
internal static class TrayHost
{
    public static async Task<bool> AvailableAsync()
    {
        if (!OperatingSystem.IsLinux()) return true;
        if (DBusAddress.Session is not { } address) return false;
        try
        {
            using var bus = new DBusConnection(address);
            await bus.ConnectAsync();
            var message = Get(bus, "org.kde.StatusNotifierWatcher", "/StatusNotifierWatcher", "org.kde.StatusNotifierWatcher", "IsStatusNotifierHostRegistered");
            return await bus.CallMethodAsync(message, (Message m, object? _) => m.GetBodyReader().ReadVariantValue().GetBool(), null);
        }
        catch (Exception e) when (e is DBusExceptionBase or InvalidOperationException)
        {
            return false; // no watcher on the bus (ServiceUnknown), or no session bus
        }
    }

    private static MessageBuffer Get(DBusConnection bus, string service, string path, string @interface, string property)
    {
        var writer = bus.GetMessageWriter();
        try
        {
            writer.WriteMethodCallHeader(service, path, "org.freedesktop.DBus.Properties", "Get", "ss", MessageFlags.None);
            writer.WriteString(@interface);
            writer.WriteString(property);
            return writer.CreateMessage();
        }
        finally
        {
            writer.Dispose();
        }
    }
}
