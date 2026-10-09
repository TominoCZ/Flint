using System.Globalization;
using System.Text.RegularExpressions;

namespace Flint.App.Setup;

/// <summary>
/// Resource names behind the decompile's obfuscated R fields. jadx names a renamed field f&lt;digits&gt;&lt;letters&gt;
/// (f42079P); the real ones are snake_case. Both kinds of field share the resource id, written in hex in some modules
/// and in decimal in others, so the id leads from one to the other.
/// </summary>
internal sealed partial class ResourceNames
{
    private readonly Dictionary<(string Kind, long Id), string> _byId = [];
    private readonly Dictionary<string, Dictionary<string, long>> _alias = [];

    public ResourceNames(JavaSources sources)
    {
        foreach (var file in Directory.EnumerateFiles(sources.Root, "R*.java", SearchOption.AllDirectories))
        {
            var fileName = Path.GetFileName(file);
            if (!RFile().IsMatch(fileName)) continue;
            var kind = fileName.StartsWith("R$") ? fileName[2..^5] : null;
            foreach (var line in File.ReadLines(file))
            {
                if (ClassLine().Match(line) is { Success: true } c)
                {
                    if (c.Groups[1].Value != "R") kind = c.Groups[1].Value.StartsWith("R$") ? c.Groups[1].Value[2..] : c.Groups[1].Value;
                }
                else if (kind is not null && Field().Match(line) is { Success: true } f)
                {
                    var (name, id) = (f.Groups[1].Value, ParseId(f.Groups[2].Value));
                    if (Obfuscated().IsMatch(name))
                    {
                        if (!_alias.TryGetValue(kind, out var fields)) _alias[kind] = fields = [];
                        fields[name] = id;
                    }
                    else
                    {
                        _byId.TryAdd((kind, id), name);
                    }
                }
            }
        }
        foreach (var ((kind, id), name) in _byId) ByRawId[id] = (kind, name);
    }

    /// <summary>Every named resource by its id, for the bare ids jadx leaves in some XML.</summary>
    public Dictionary<long, (string Kind, string Name)> ByRawId { get; } = [];

    /// <summary>The real name of an R field, or the field itself when it is not renamed.</summary>
    public string Real(string kind, string field) =>
        _alias.GetValueOrDefault(kind)?.TryGetValue(field, out var id) == true && _byId.TryGetValue((kind, id), out var name) ? name : field;

    /// <summary>The drawable or layout an expression names (<c>R.drawable.x</c>, <c>R$drawable.f123</c>), or null.</summary>
    public string? Ref(string token) =>
        Reference().Match(token) is { Success: true } m ? Real(m.Groups[1].Value, m.Groups[2].Value) : null;

    private static long ParseId(string text) =>
        text.StartsWith("0x") ? long.Parse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture) : long.Parse(text, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^R(\$\w+)?\.java$")]
    private static partial Regex RFile();

    [GeneratedRegex(@"class (\w+) \{")]
    private static partial Regex ClassLine();

    [GeneratedRegex(@"public static (?:final )?int (\w+) = (0x[0-9a-fA-F]+|\d+);")]
    private static partial Regex Field();

    [GeneratedRegex(@"^f\d+\w*$")]
    private static partial Regex Obfuscated();

    [GeneratedRegex(@"R[.$](drawable|layout)\.(\w+)")]
    private static partial Regex Reference();
}
