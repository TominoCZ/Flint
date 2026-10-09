namespace Flint.Core.Protocol;

/// <summary>
/// Reassembles chunks from bytes that arrive in arbitrary pieces (BLE notifications, log lines) and may carry
/// 16-byte <c>01 FE …</c> block headers anywhere, including in the middle of a chunk: the amp splits long
/// chunks across blocks. 0xFE never occurs inside a chunk (the payload is 7-bit, and
/// no cmd/sub reaches 0xFE), so <c>01 FE</c> always starts a header.
/// </summary>
public sealed class ChunkStream
{
    private const int BlockHeaderLength = 16;
    private readonly List<byte> _buffer = [];

    /// <summary>Feeds bytes and returns every chunk they complete, F0 through F7, with block headers removed.</summary>
    public IReadOnlyList<byte[]> Push(ReadOnlySpan<byte> bytes)
    {
        _buffer.AddRange(bytes);
        var chunks = new List<byte[]>();
        while (true)
        {
            var header = IndexOfHeader();
            if (header >= 0 && _buffer.Count - header >= BlockHeaderLength)
            {
                _buffer.RemoveRange(header, BlockHeaderLength);
                continue;
            }

            // Nothing past a header that is still arriving is safe to look at yet.
            var limit = header >= 0 ? header : _buffer.Count;
            var start = _buffer.IndexOf(Frame.Start, 0, limit);
            _buffer.RemoveRange(0, start >= 0 ? start : limit); // bytes outside any chunk are noise
            if (start < 0) return chunks;

            var end = _buffer.IndexOf(Frame.End, 0, limit - start);
            if (end < 0) return chunks;
            chunks.Add([.. _buffer.GetRange(0, end + 1)]);
            _buffer.RemoveRange(0, end + 1);
        }
    }

    private int IndexOfHeader()
    {
        for (var i = 0; i + 1 < _buffer.Count; i++)
            if (_buffer[i] == 0x01 && _buffer[i + 1] == 0xFE) return i;
        return -1;
    }
}
