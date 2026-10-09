// Decodes the official Spark app's Bluetooth traffic out of logcat, one line per chunk; other lines pass through.
// Usage: adb logcat -v threadtime … | SparkLog [research/native/opcode-table.txt]   (run by scripts/spark-log.sh)
using Flint.Core.Protocol;
using SparkLog;

var formatter = new ChunkFormatter(OpcodeNames.Load(args.FirstOrDefault()));
var toAmp = new ChunkStream();
var fromAmp = new ChunkStream();

while (Console.ReadLine() is { } text)
{
    if (BleLogLine.TryParse(text) is not { IsTraffic: true } line)
    {
        Console.WriteLine($"   {text}");
        continue;
    }
    var outbound = line.ToAmp == true;
    foreach (var raw in (outbound ? toAmp : fromAmp).Push(line.Bytes))
        Console.WriteLine(formatter.Format(line.Time, outbound, raw));
}
