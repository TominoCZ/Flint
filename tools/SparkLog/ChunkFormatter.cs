using System.Text;
using System.Text.RegularExpressions;
using Flint.Core.Protocol;

namespace SparkLog;

/// <summary>One readable line per chunk: time, direction, seq, opcode and its type, payload, and any text in it.</summary>
public sealed partial class ChunkFormatter(OpcodeNames names)
{
    public string Format(string time, bool toAmp, byte[] raw)
    {
        var direction = toAmp ? "→ amp" : "← amp";
        if (!Frame.TryDecodeChunk(raw, out var chunk))
            return $"{time} {direction}  BAD CHUNK {Convert.ToHexStringLower(raw)}";

        var payload = chunk!.Payload;
        return $"{time} {direction}  seq={chunk.Seq:x2}  {chunk.Cmd:x2} {chunk.Sub:x2}  {names[chunk.Cmd, chunk.Sub],-34}" +
               $"{Part(chunk)}  [{payload.Length}] {Convert.ToHexStringLower(payload)}{Strings(payload)}";
    }

    // Presets travel as multi-chunk messages whose payload starts with <total> <index> <length> (research.md §4).
    private static string Part(Chunk chunk) =>
        chunk.Sub == 0x01 && chunk.Cmd is 0x01 or 0x03 && chunk.Payload.Length >= 3
            ? $" part {chunk.Payload[1] + 1}/{chunk.Payload[0]}"
            : "";

    // Printable runs of 3+ characters: preset, pedal and amp names are what make a dump readable.
    private static string Strings(byte[] payload)
    {
        var runs = Printable().Matches(Encoding.Latin1.GetString(payload)).Select(r => r.Value).ToList();
        return runs.Count == 0 ? "" : "  \"" + string.Join("\" \"", runs) + "\"";
    }

    [GeneratedRegex(@"[\x20-\x7e]{3,}")]
    private static partial Regex Printable();
}
