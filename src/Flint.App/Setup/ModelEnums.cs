using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Flint.App.Setup;

/// <summary>
/// The models the Spark app offers, read from its code: the <c>Fx*</c> enums (one per catalog class), the reverb types
/// that param 6 of bias.reverb picks, and the knob styles. Only facts come out: ids, names, file names.
/// </summary>
internal static partial class ModelEnums
{
    // Constructor arguments after the enum's own (name, ordinal), per catalog class. Read from the constructors.
    private static readonly Dictionary<string, string?[]> Fields = new()
    {
        ["FxNoiseGate"] = ["dspId", "name", "icon", null],
        ["FxComp"] = ["dspId", "name", "icon", "itemIcon", null],
        ["FxDrive"] = ["dspId", "name", "icon", "itemIcon", null],
        ["FxAmp"] = ["dspId", "name", "icon", "itemIcon", "face", "knobStyle", "cab", null],
        ["FxPreamp"] = ["dspId", "name", "icon", "itemIcon", "face", "knobStyle", "cab", null],
        ["FxModulation"] = ["dspId", "name", "icon", "itemIcon", null],
        ["FxDelay"] = ["dspId", "name", "icon", "itemIcon", null],
        ["FxReverb"] = ["dspId", null],
    };

    /// <summary>The enum's entries, in order, with their constructor arguments resolved.</summary>
    public static List<JsonObject> Entries(string cls, JavaSources sources, ResourceNames names, List<string> errors)
    {
        var text = sources.Model(cls);
        var inline = !text.Contains($"new {cls}("); // FxNoiseGate declares its one entry inline, without name and ordinal
        var block = inline ? text : StaticBlock().Match(text).Groups[1].Value;
        // Locals are reassigned as the block goes (int i10 = R.drawable.x; Knob.styleRes styleres = ...), so read in order.
        var pattern = inline
            ? @"^    (\w+)\((\"".*?)\)[,;]$"
            : $@"(?:(?:int|Integer|Knob\.styleRes|a) (\w+) = ([^;]+);)|(?:(\w+) = new {cls}\((.*?)\);)";
        var locals = new Dictionary<string, string>();
        var entries = new List<JsonObject>();
        foreach (Match m in Regex.Matches(block, pattern, RegexOptions.Multiline))
        {
            if (!inline && m.Groups[1].Success)
            {
                locals[m.Groups[1].Value] = m.Groups[2].Value;
                continue;
            }
            var (id, argText) = inline ? (m.Groups[1].Value, m.Groups[2].Value) : (m.Groups[3].Value, m.Groups[4].Value);
            var args = JavaSources.SplitArgs(argText);
            var entry = new JsonObject { ["id"] = id };
            foreach (var (field, raw) in Fields[cls].Zip(inline ? args : args.Skip(2)))
            {
                if (field is null) continue;
                var arg = locals.GetValueOrDefault(raw, raw);
                if (arg.StartsWith('"'))
                {
                    entry[field] = arg.Trim('"');
                }
                else if (arg.Contains("styleRes."))
                {
                    entry[field] = arg.Split("styleRes.")[1];
                }
                else if (arg != "null")
                {
                    var name = names.Ref(arg);
                    entry[field] = name;
                    if (name is null) errors.Add($"{cls}.{id}: cannot resolve {field} = {arg}");
                }
            }
            entries.Add(entry);
        }
        if (entries.Count == 0) errors.Add($"{cls}: no entries found");
        return entries;
    }

    /// <summary>
    /// Param 6 of bias.reverb picks the reverb: value -> name, face, strip and picker icons. Each is an if-chain over the
    /// value (<c>if (m.b(param, 0.4f)) { return ... }</c>, the last one sometimes a ternary).
    /// </summary>
    public static List<JsonObject> ReverbTypes(JavaSources sources, ResourceNames names, List<string> errors)
    {
        var types = new SortedDictionary<double, JsonObject>();
        foreach (Match m in ReverbHit().Matches(sources.Model("FxReverb")))
        {
            var value = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            var token = m.Groups[2].Value;
            var name = names.Ref(token);
            if (!token.StartsWith('"') && name is null)
            {
                errors.Add($"reverb type {value}: cannot resolve {token}");
                continue;
            }
            var field = token.StartsWith('"') ? "name"
                : name!.StartsWith("editor_") ? "face"
                : name.StartsWith("module_item_icon") ? "itemIcon"
                : "icon";
            if (!types.TryGetValue(value, out var type)) types[value] = type = new JsonObject { ["value"] = value };
            type[field] = field == "name" ? token.Trim('"') : name;
        }
        foreach (var type in types.Values.Where(t => t.Count != 5))
            errors.Add($"reverb type {type["value"]}: only {string.Join(", ", type.Select(p => p.Key).Order(StringComparer.Ordinal))}");
        return [.. types.Values];
    }

    /// <summary>Each knob style the amp faces name, with its drawable.</summary>
    public static JsonObject KnobStyles(JavaSources sources, ResourceNames names)
    {
        var styles = new JsonObject();
        foreach (Match m in KnobStyle().Matches(sources.Find("Knob.java", folder: "controls")))
            styles[m.Groups[1].Value] = names.Ref(m.Groups[2].Value);
        return styles;
    }

    [GeneratedRegex(@"\n    static \{(.*?)\n    \}", RegexOptions.Singleline)]
    private static partial Regex StaticBlock();

    [GeneratedRegex(@"m\.b\(\w+, ([0-9.]+)f\)\)?\s*(?:\{\s*return|\?)\s*(""[^""]*""|R\.drawable\.\w+|Integer\.valueOf\(R\.drawable\.\w+\))")]
    private static partial Regex ReverbHit();

    [GeneratedRegex(@"(JamUpKnob_\d+)\(\d+, (R\$drawable\.\w+)\)")]
    private static partial Regex KnobStyle();
}
