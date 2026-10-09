using System.Net.Sockets;
using Flint.Core.Logging;

namespace Flint.App;

/// <summary>
/// One running app per user: a second launch asks the first to show its window and exits, rather than fight
/// it for the amp. Whoever holds <c>instance.lock</c> is first (the OS lets go of it when the process dies, however it
/// dies); it listens on <c>instance.sock</c>, where any connection means "show the window".
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private readonly FileStream _lock;
    private readonly Socket _listener;

    private SingleInstance(FileStream lockFile, Socket listener)
    {
        (_lock, _listener) = (lockFile, listener);
        _ = AcceptAsync();
    }

    /// <summary>Raised on a thread-pool thread each time another launch asks for the window.</summary>
    public event Action? Activated;

    /// <summary>
    /// Claims the folder for this process, or returns null after asking the instance that holds it to show its window.
    /// Either side may be mid-start or mid-exit (an exit takes up to 5 s to release the amp), so this keeps trying both
    /// for a few seconds.
    /// </summary>
    public static SingleInstance? Claim(string folder)
    {
        Directory.CreateDirectory(folder);
        var socketPath = Path.Combine(folder, "instance.sock");
        for (var attempt = 0; attempt < 30; attempt++, Thread.Sleep(250))
        {
            if (TryLock(Path.Combine(folder, "instance.lock")) is { } lockFile)
            {
                File.Delete(socketPath); // left behind by the last first instance
                var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                listener.Bind(new UnixDomainSocketEndPoint(socketPath));
                listener.Listen();
                return new SingleInstance(lockFile, listener);
            }
            if (TryKnock(socketPath))
            {
                Log.Info("app", "already running: showed its window");
                return null;
            }
        }
        Log.Warn("app", "already running, but it did not answer");
        return null;
    }

    private static FileStream? TryLock(string path)
    {
        try
        {
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static bool TryKnock(string socketPath)
    {
        try
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.Connect(new UnixDomainSocketEndPoint(socketPath));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (true)
            {
                using var knock = await _listener.AcceptAsync();
                Activated?.Invoke();
            }
        }
        catch (Exception e) when (e is SocketException or ObjectDisposedException)
        {
            // disposed on exit
        }
    }

    public void Dispose()
    {
        _listener.Dispose();
        _lock.Dispose();
    }
}
