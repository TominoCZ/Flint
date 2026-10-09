using Flint.Core.Logging;
using Flint.Core.Protocol.Payloads;

namespace Flint.Core.Session;

/// <summary>
/// Knob drags produce hundreds of <c>01 04</c> values and the amp acks none of them, so only the latest value per
/// (module, param) is sent, at most once per <paramref name="interval"/>.
/// </summary>
internal sealed class ParameterCoalescer(Func<DspParam, Task> send, TimeSpan interval)
{
    private readonly Dictionary<(string, int, int), DspParam> _latest = [];
    private bool _flushing;
    private DateTime _lastFlush;

    public void Set(DspParam value)
    {
        lock (_latest)
        {
            _latest[(value.Module, value.Param, value.SignalChain)] = value;
            if (_flushing) return;
            _flushing = true;
        }
        _ = FlushAsync();
    }

    private async Task FlushAsync()
    {
        while (true)
        {
            var wait = _lastFlush + interval - DateTime.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait);
            DspParam[] batch;
            lock (_latest)
            {
                if (_latest.Count == 0)
                {
                    _flushing = false;
                    return;
                }
                batch = [.. _latest.Values];
                _latest.Clear();
            }
            _lastFlush = DateTime.UtcNow;
            foreach (var value in batch)
            {
                try
                {
                    await send(value);
                }
                catch (Exception e)
                {
                    // A lost knob value is superseded by the next one; the link's own drop handling covers the rest.
                    Log.Warn("amp", $"knob {value.Module} {value.Param} not sent: {e.Message}");
                }
            }
        }
    }
}
