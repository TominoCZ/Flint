using Flint.Core.Connection;
using Flint.Core.Protocol;

namespace Flint.Core.Tests;

/// <summary>
/// An amp in memory. Records every chunk written to it, answers through <see cref="Respond"/> (synchronously, so a
/// reply can beat the write's own return, as it may on a real radio), and sends chunks back in 20-byte notifications.
/// </summary>
public sealed class FakeLink : ILink
{
    private readonly ChunkStream _incoming = new();
    private readonly List<Chunk> _written = [];

    public Func<Chunk, IEnumerable<Chunk>> Respond { get; set; } = _ => [];
    public Exception? ConnectError { get; set; }
    public bool HangOnConnect { get; set; }
    public bool Connected { get; private set; }
    public bool Disposed { get; private set; }

    public IReadOnlyList<Chunk> Written
    {
        get { lock (_written) return [.. _written]; }
    }

    public event Action<byte[]>? Received;
    public event Action<string>? Dropped;

    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (ConnectError is not null) throw ConnectError;
        if (HangOnConnect) return Task.Delay(Timeout.Infinite, cancellationToken);
        Connected = true;
        return Task.CompletedTask;
    }

    public Task WriteAsync(ReadOnlyMemory<byte> block, CancellationToken cancellationToken)
    {
        Assert.True(Connected && !Disposed, "write on a link that is not connected");
        var bytes = block.ToArray();
        Assert.Equal([0x01, 0xFE, 0x00, 0x00, 0x53, 0xFE, (byte)bytes.Length], bytes[..7]); // every write is one block
        foreach (var raw in _incoming.Push(bytes))
        {
            Assert.True(Frame.TryDecodeChunk(raw, out var chunk));
            lock (_written) _written.Add(chunk!);
            foreach (var reply in Respond(chunk!)) Send(reply);
        }
        return Task.CompletedTask;
    }

    /// <summary>The amp sends a chunk: bare, split into notifications of at most 20 bytes.</summary>
    public void Send(Chunk chunk)
    {
        foreach (var notification in Frame.EncodeChunk(chunk).Chunk(20)) Received?.Invoke(notification);
    }

    public void Drop(string reason) => Dropped?.Invoke(reason);

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
