using Flint.Core.Protocol;
using SparkLog;

namespace Flint.Core.Tests;

/// <summary>A decoded chunk from a capture, and which way it went.</summary>
public sealed record Captured(bool ToAmp, Chunk Chunk);

/// <summary>Reads the logcat excerpts under Captures/ (made with scripts/spark-log.sh) back into chunks.</summary>
public static class CaptureFile
{
    public static List<Captured> Read(string name)
    {
        var toAmp = new ChunkStream();
        var fromAmp = new ChunkStream();
        var chunks = new List<Captured>();
        foreach (var line in File.ReadLines(Path.Combine("Captures", name + ".txt")).Select(BleLogLine.TryParse))
        {
            Assert.True(line is { IsTraffic: true }, $"{name}: not a traffic line");
            var outbound = line!.ToAmp == true;
            foreach (var raw in (outbound ? toAmp : fromAmp).Push(line.Bytes))
            {
                Assert.True(Frame.TryDecodeChunk(raw, out var chunk), $"{name}: bad chunk {Convert.ToHexString(raw)}");
                chunks.Add(new Captured(outbound, chunk!));
            }
        }
        return chunks;
    }
}
