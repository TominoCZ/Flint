using System.Globalization;
using System.Text.RegularExpressions;

namespace Flint.App.Setup;

/// <summary>
/// jadx leaves some references in layouts and XML drawables as bare ids ("0x7f080262"), and styles name some drawables
/// as "?unknown_ref: 7f0804fe". Naming them once here lets the app read only @kind/name references.
/// </summary>
internal static partial class RawIds
{
    /// <summary>Rewrites the files under <paramref name="res"/>; returns how many changed.</summary>
    public static int Name(string res, ResourceNames names)
    {
        var named = 0;
        var xml = Directory.EnumerateDirectories(res, "layout*").Concat(Directory.EnumerateDirectories(res, "drawable*"))
            .SelectMany(d => Directory.EnumerateFiles(d, "*.xml"));
        foreach (var file in xml)
            named += Rewrite(file, QuotedId(), m => $"\"{Named(m.Groups[1].Value[2..], names) ?? m.Groups[1].Value}\"");
        named += Rewrite(Path.Combine(res, "values", "styles.xml"), UnknownRef(), m => Named(m.Groups[1].Value, names) ?? m.Value);
        return named;
    }

    private static string? Named(string hex, ResourceNames names) =>
        names.ByRawId.TryGetValue(long.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture), out var r) ? $"@{r.Kind}/{r.Name}" : null;

    private static int Rewrite(string file, Regex pattern, MatchEvaluator name)
    {
        var text = File.ReadAllText(file);
        var fixedText = pattern.Replace(text, name);
        if (fixedText == text) return 0;
        File.WriteAllText(file, fixedText);
        return 1;
    }

    [GeneratedRegex(@"""(0x7f[0-9a-f]{6})""")]
    private static partial Regex QuotedId();

    [GeneratedRegex(@"\?unknown_ref: (7f[0-9a-f]{6})")]
    private static partial Regex UnknownRef();
}
