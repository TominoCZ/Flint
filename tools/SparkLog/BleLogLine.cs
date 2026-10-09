using System.Text.RegularExpressions;

namespace SparkLog;

/// <summary>
/// One line of <c>adb logcat -v threadtime</c> from the official app. Its Bluetooth lines carry raw bytes:
/// <c>BLEDATA : send data : 01 fe …</c> for each block written, <c>[PGBLE] : receive data: f0 01 …</c> for each
/// notification (docs/research.md §3). Everything else is context.
/// </summary>
public sealed partial record BleLogLine(string Time, string Tag, string Message, bool? ToAmp, byte[] Bytes)
{
    public bool IsTraffic => ToAmp is not null;

    public static BleLogLine? TryParse(string line)
    {
        var m = Header().Match(line);
        if (!m.Success) return null;
        var message = m.Groups["msg"].Value;
        var traffic = Traffic().Match(message);
        return new BleLogLine(m.Groups["time"].Value, m.Groups["tag"].Value, message,
            traffic.Success ? traffic.Groups["dir"].Value == "send" : null,
            traffic.Success ? Convert.FromHexString(traffic.Groups["hex"].Value.Replace(" ", "")) : []);
    }

    // "2026-10-07 19:41:42.235 19018 25082 I BLEDATA : send data : 01 fe …" (date, time, pid, tid, level, tag)
    [GeneratedRegex(@"^\S+ (?<time>\S+)\s+\d+\s+\d+ \w (?<tag>.+?)\s*: (?<msg>.*)$")]
    private static partial Regex Header();

    [GeneratedRegex(@"^(?<dir>send|receive) data\s*:\s*(?<hex>(?:[0-9a-f]{2} ?)+)$")]
    private static partial Regex Traffic();
}
