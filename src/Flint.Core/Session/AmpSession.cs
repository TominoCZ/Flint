using Flint.Core.Connection;
using Flint.Core.Logging;
using Flint.Core.Presets;
using Flint.Core.Protocol;
using Flint.Core.Protocol.Payloads;

namespace Flint.Core.Session;

/// <summary>
/// The protocol over one connected <see cref="ILink"/>: frames requests, matches replies to them, and
/// raises everything else as <see cref="Notified"/>. One request is in flight at a time, as the official app does it,
/// so correlation never races. Knob values are coalesced; preset transfers hold the turn for all their pieces.
/// </summary>
public sealed class AmpSession : IDisposable
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2), KnobInterval = TimeSpan.FromMilliseconds(30);

    private readonly ILink _link;
    private readonly TimeSpan _timeout;
    private readonly ChunkStream _stream = new();
    private readonly SemaphoreSlim _turn = new(1, 1);
    private readonly ParameterCoalescer _knobs;
    private volatile Transaction? _pending;
    private volatile string? _dropped;
    private byte _seq;

    /// <param name="timeout">How long a request may wait with no reply arriving; a reply in pieces restarts it on each.</param>
    public AmpSession(ILink link, TimeSpan? timeout = null)
    {
        _link = link;
        _timeout = timeout ?? DefaultTimeout;
        _knobs = new ParameterCoalescer(p => SendAsync(Opcode.SetParameter, p), KnobInterval);
        link.Received += OnReceived;
        link.Dropped += OnDropped;
    }

    /// <summary>A chunk from the amp that answers none of our requests: its own knobs, buttons, tuner, looper.</summary>
    public event Action<Chunk, IPayload?>? Notified;

    /// <summary>When the last chunk of any kind arrived (UTC), for the supervisor's keepalive.</summary>
    public DateTime LastReceived { get; private set; } = DateTime.UtcNow;

    /// <summary>Sends a request and returns its one reply: <c>03 xx</c> data for a get, <c>04 xx</c> for an acked set.</summary>
    public async Task<Chunk> RequestAsync(int op, IPayload? payload = null, CancellationToken cancellationToken = default)
    {
        await _turn.WaitAsync(cancellationToken);
        try
        {
            return (await ExchangeAsync(NewChunk(op, payload), _ => true, cancellationToken))[0];
        }
        finally
        {
            _turn.Release();
        }
    }

    /// <summary><see cref="RequestAsync"/>, with the reply decoded as <typeparamref name="T"/>.</summary>
    public async Task<T> GetAsync<T>(int op, IPayload? payload = null, CancellationToken cancellationToken = default) where T : IPayload
    {
        var reply = await RequestAsync(op, payload, cancellationToken);
        IPayload? decoded;
        try
        {
            decoded = PayloadDecoder.Decode(reply);
        }
        catch (FormatException e)
        {
            // With the bytes, so a reply the amp rarely sends can be pinned down from the log.
            throw new FormatException($"{reply.Op:x4} {Convert.ToHexStringLower(reply.Payload)}: {e.Message}", e);
        }
        return decoded is T value ? value : throw new FormatException($"{op:x4} was answered with {reply.Op:x4}, not {typeof(T).Name}");
    }

    /// <summary>Sets a knob. Not acked by the amp; sent within <see cref="KnobInterval"/>, latest value wins.</summary>
    public void SetParameter(DspParam value) => _knobs.Set(value);

    /// <summary><c>02 01</c>, then <c>03 01</c> pieces until the last one.</summary>
    public async Task<(PresetType Type, int Slot, Preset Preset)> DownloadPresetAsync(PresetType type, int slot,
        CancellationToken cancellationToken = default)
    {
        await _turn.WaitAsync(cancellationToken);
        try
        {
            var pieces = await ExchangeAsync(NewChunk(Opcode.DownloadPreset, new PresetRequest(type, slot)),
                reply => PresetTransfer.IsLast(reply.Payload), cancellationToken);
            return PresetTransfer.Unwrap(PresetTransfer.Join([.. pieces.Select(p => p.Payload)]));
        }
        finally
        {
            _turn.Release();
        }
    }

    /// <summary>
    /// <c>01 01</c> pieces under one seq, each acked with <c>05 01</c> before the next goes. Sends
    /// <see cref="PresetTransfer.ForUpload"/>'s form, so presets downloaded from the amp go back with the app's floats.
    /// </summary>
    public async Task UploadPresetAsync(PresetType type, int slot, Preset preset, CancellationToken cancellationToken = default)
    {
        var pieces = PresetTransfer.Split(PresetTransfer.Wrap(type, slot, PresetTransfer.ForUpload(preset)), PresetTransfer.UploadPieceSize);
        await _turn.WaitAsync(cancellationToken);
        try
        {
            var seq = NextSeq();
            foreach (var piece in pieces)
                await ExchangeAsync(new Chunk(seq, Opcode.UploadPreset >> 8, Opcode.UploadPreset & 0xFF, piece), _ => true,
                    cancellationToken);
        }
        finally
        {
            _turn.Release();
        }
    }

    public void Dispose()
    {
        _link.Received -= OnReceived;
        _link.Dropped -= OnDropped;
        OnDropped("session closed");
    }

    /// <summary>
    /// A request the amp does not answer (a knob value), sent now and in order with the others, not through the knob
    /// coalescer: a reverb type must reach the amp before the <c>01 15</c> that follows it, as the official app sends them.
    /// </summary>
    public async Task SendAsync(int op, IPayload payload)
    {
        await _turn.WaitAsync();
        try
        {
            ThrowIfDropped();
            await _link.WriteAsync(Frame.WrapBlock(Frame.EncodeChunk(NewChunk(op, payload))), CancellationToken.None);
        }
        finally
        {
            _turn.Release();
        }
    }

    /// <summary>Writes one chunk and waits until <paramref name="isLast"/> accepts a reply. The caller holds the turn.</summary>
    private async Task<IReadOnlyList<Chunk>> ExchangeAsync(Chunk request, Func<Chunk, bool> isLast, CancellationToken cancellationToken)
    {
        ThrowIfDropped();
        var transaction = new Transaction(request, isLast);
        _pending = transaction;
        try
        {
            await _link.WriteAsync(Frame.WrapBlock(Frame.EncodeChunk(request)), cancellationToken);
            while (true)
            {
                var seen = transaction.Received;
                var finished = await Task.WhenAny(transaction.Done, Task.Delay(_timeout, cancellationToken));
                if (finished == transaction.Done) return await transaction.Done;
                cancellationToken.ThrowIfCancellationRequested();
                if (transaction.Received == seen)
                    throw new TimeoutException($"no reply to {request.Op:x4} within {_timeout.TotalMilliseconds:F0} ms");
            }
        }
        finally
        {
            _pending = null;
        }
    }

    private void OnReceived(byte[] bytes)
    {
        IReadOnlyList<byte[]> raws;
        lock (_stream) raws = _stream.Push(bytes);
        foreach (var raw in raws)
        {
            if (!Frame.TryDecodeChunk(raw, out var chunk))
            {
                Log.Warn("amp", $"bad chunk {Convert.ToHexStringLower(raw)}");
                continue;
            }
            LastReceived = DateTime.UtcNow;
            if (_pending?.TryAccept(chunk!) == true) continue;
            Notified?.Invoke(chunk!, TryDecode(chunk!));
        }
    }

    private void OnDropped(string reason)
    {
        _dropped ??= reason;
        _pending?.Fail(new IOException($"link dropped: {reason}"));
    }

    private void ThrowIfDropped()
    {
        if (_dropped is not null) throw new IOException($"link dropped: {_dropped}");
    }

    private static IPayload? TryDecode(Chunk chunk)
    {
        try
        {
            return PayloadDecoder.Decode(chunk);
        }
        catch (FormatException e)
        {
            Log.Warn("amp", $"{chunk.Op:x4} undecodable: {e.Message}");
            return null;
        }
    }

    /// <summary>Call with the turn held, so seqs go out in order.</summary>
    private Chunk NewChunk(int op, IPayload? payload) => new(NextSeq(), (byte)(op >> 8), (byte)op, payload?.Encode() ?? []);

    /// <summary>01 to 3F and round again, as the official app counts.</summary>
    private byte NextSeq() => _seq = (byte)(_seq % 0x3F + 1);
}
