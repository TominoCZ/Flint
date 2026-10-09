using Flint.Core.Connection;
using Flint.Core.Protocol;
using SparkLog;

namespace BluezProbe;

/// <summary>Prints every chunk that crosses the wrapped link, both ways, in the live decoder's format.</summary>
internal sealed class TracingLink(ILink inner) : ILink
{
    private static readonly ChunkFormatter Formatter = new(OpcodeNames.Load("research/native/opcode-table.txt"));
    private readonly ChunkStream _in = new(), _out = new();

    public event Action<byte[]>? Received
    {
        add => inner.Received += value;
        remove => inner.Received -= value;
    }

    public event Action<string>? Dropped
    {
        add => inner.Dropped += value;
        remove => inner.Dropped -= value;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        inner.Received += bytes => Print(_in, bytes, toAmp: false);
        await inner.ConnectAsync(cancellationToken);
    }

    public Task WriteAsync(ReadOnlyMemory<byte> block, CancellationToken cancellationToken)
    {
        Print(_out, block.Span, toAmp: true);
        return inner.WriteAsync(block, cancellationToken);
    }

    public ValueTask DisposeAsync() => inner.DisposeAsync();

    private static void Print(ChunkStream stream, ReadOnlySpan<byte> bytes, bool toAmp)
    {
        lock (stream)
            foreach (var raw in stream.Push(bytes))
                Console.WriteLine(Formatter.Format($"{DateTime.Now:HH:mm:ss.fff}", toAmp, raw));
    }
}
