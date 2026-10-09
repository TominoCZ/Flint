using Flint.Core.Protocol;

namespace Flint.Core.Tests;

/// <summary>
/// Plays the amp's side of a capture to a <see cref="FakeLink"/>: each chunk written must be the next one the
/// official app sent (seq aside), and is answered with what the amp sent after it, re-sequenced to the seq we used.
/// </summary>
public sealed class CaptureReplay
{
    private readonly Queue<(Chunk Request, List<Chunk> Replies)> _exchanges = [];

    public CaptureReplay(string name)
    {
        foreach (var (toAmp, chunk) in CaptureFile.Read(name))
        {
            if (toAmp) _exchanges.Enqueue((chunk, []));
            else if (_exchanges.Count > 0) _exchanges.Last().Replies.Add(chunk);
        }
    }

    public int Remaining => _exchanges.Count;

    /// <summary>Drops exchanges up to the first request with this opcode, to start a test mid-capture.</summary>
    public CaptureReplay SkipTo(int op)
    {
        while (_exchanges.Peek().Request.Op != op) _exchanges.Dequeue();
        return this;
    }

    public IEnumerable<Chunk> Respond(Chunk written)
    {
        Assert.True(_exchanges.Count > 0, $"unexpected write {written.Op:x4}");
        var (request, replies) = _exchanges.Dequeue();
        Assert.Equal(request.Op, written.Op);
        Assert.Equal(Convert.ToHexString(request.Payload), Convert.ToHexString(written.Payload));
        return replies.Select(r => r.Seq == request.Seq ? r with { Seq = written.Seq } : r);
    }
}
