using System.Text.RegularExpressions;

namespace SparkLog;

/// <summary>
/// Names opcodes after libpghw's payload types, from research/native/opcode-table.txt (scripts/opcode-table.py).
/// A row reads: <c>01 06  CReplaceDspModule -> 04 06  CNull  true</c>.
/// </summary>
public sealed partial class OpcodeNames
{
    private readonly Dictionary<string, string> _names = [];

    public static OpcodeNames Load(string? path)
    {
        var names = new OpcodeNames();
        if (path is null || !File.Exists(path)) return names;
        foreach (var match in File.ReadLines(path).Select(row => Row().Match(row)).Where(m => m.Success))
        {
            var request = match.Groups["req"].Value;
            names._names[request] = match.Groups["reqType"].Value;
            if (match.Groups["rep"].Value != "-")
                names._names.TryAdd(match.Groups["rep"].Value, $"{match.Groups["repType"].Value} (re {request})");
        }
        return names;
    }

    public string this[byte cmd, byte sub] => _names.GetValueOrDefault($"{cmd:x2} {sub:x2}", "?");

    [GeneratedRegex(@"^(?<req>\w\w \w\w)\s+(?<reqType>\S+)\s+->\s+(?<rep>\w\w \w\w|-)\s+(?<repType>\S+)")]
    private static partial Regex Row();
}
