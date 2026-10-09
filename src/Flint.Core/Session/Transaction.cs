using Flint.Core.Protocol;

namespace Flint.Core.Session;

/// <summary>
/// One request waiting for its reply. A reply carries the request's seq and sub with cmd 03 (data), 04 (ack) or
/// 05 (upload-piece ack); the amp's own notifies count their seq from 0x40, so they never match.
/// </summary>
internal sealed class Transaction(Chunk request, Func<Chunk, bool> isLast)
{
    private readonly List<Chunk> _replies = [];
    private readonly TaskCompletionSource<IReadOnlyList<Chunk>> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Chunk Request => request;

    public Task<IReadOnlyList<Chunk>> Done => _done.Task;

    /// <summary>Replies so far, so a waiter can tell a slow multi-chunk reply from a dead one.</summary>
    public int Received
    {
        get { lock (_replies) return _replies.Count; }
    }

    /// <summary>Takes the chunk if it answers this request; false means it is someone else's (a notify).</summary>
    public bool TryAccept(Chunk chunk)
    {
        if (chunk.Seq != request.Seq || chunk.Sub != request.Sub || chunk.Cmd is not (0x03 or 0x04 or 0x05)) return false;
        lock (_replies)
        {
            _replies.Add(chunk);
            if (isLast(chunk)) _done.TrySetResult([.. _replies]);
        }
        return true;
    }

    public void Fail(Exception error) => _done.TrySetException(error);
}
