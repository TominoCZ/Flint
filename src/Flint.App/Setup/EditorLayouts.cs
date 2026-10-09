using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Flint.App.Setup;

/// <summary>Which layout each model's editor inflates, read from the factory the enum's <c>editor()</c> calls.</summary>
internal static partial class EditorLayouts
{
    /// <summary>Model id -> its editor layouts (a phone and a tablet one for some), from a switch, if-chain, ternary or single layout.</summary>
    public static Dictionary<string, List<string>> Read(string cls, List<JsonObject> entries, JavaSources sources, ResourceNames names,
                                                        List<string> errors)
    {
        var text = sources.Model(cls);
        var body = EditorBody().Match(text).Groups[1].Value;
        var ordinals = @"iArr\[" + cls + @"\.(\w+)\.ordinal\(\)\] = (\d+);";

        // Some models skip the factory for a fragment of their own (FxComp: the two wahs), chosen by a switch-map case.
        var ownCases = new Dictionary<string, string>();
        foreach (Match m in Regex.Matches(text, ordinals)) ownCases[m.Groups[2].Value] = m.Groups[1].Value;
        var special = new Dictionary<string, List<string?>>();
        foreach (var m in OwnFragmentTernary().Matches(body).Concat(OwnFragmentIf().Matches(body)))
        {
            var fragment = sources.Find(m.Groups[2].Value + ".java");
            // They inflate through generated view bindings (one for phones, one for tablets), which hold the layout ids.
            var bindings = ViewBinding().Matches(fragment).Select(b => b.Groups[1].Value).ToHashSet();
            var texts = bindings.Select(b => sources.Find(b + ".java")).Prepend(fragment);
            special[ownCases[m.Groups[1].Value]] = [.. texts.SelectMany(t => LayoutRef().Matches(t)).Select(r => names.Ref(r.Value))];
        }

        var factory = FactoryCall().Match(body).Groups[1].Value;
        var code = sources.Class(Regex.Match(text, $@"import ([\w.]+\.{factory});").Groups[1].Value);
        var caseToId = new Dictionary<string, string>();
        foreach (Match m in Regex.Matches(code, ordinals)) caseToId[m.Groups[2].Value] = m.Groups[1].Value;

        // Case numbers, and "default", to the layout each one sets.
        var byCase = new Dictionary<string, string>();
        var pending = new HashSet<string>();
        foreach (var line in code.Split('\n').Select(l => l.Trim()))
        {
            Match m;
            if ((m = Case().Match(line)).Success)
            {
                pending.Add(m.Groups[1].Value);
            }
            else if (line == "default:" || line.StartsWith("} else {"))
            {
                pending.Add("default");
            }
            else if ((m = IfCase().Match(line)).Success)
            {
                pending = [m.Groups[1].Value];
            }
            else if ((m = TernaryLayouts().Match(line)).Success)
            {
                byCase[m.Groups[1].Value] = names.Real("layout", m.Groups[2].Value);
                byCase["default"] = names.Real("layout", m.Groups[3].Value);
            }
            else if ((m = LayoutAssignment().Match(line)).Success && pending.Count > 0)
            {
                foreach (var c in pending) byCase[c] = names.Real("layout", m.Groups[1].Value);
                pending = [];
            }
            else if ((m = InflateCall().Match(line)).Success)
            {
                byCase.TryAdd("default", names.Real("layout", m.Groups[1].Value));
            }
        }

        var layouts = new Dictionary<string, List<string>>();
        foreach (var entry in entries)
        {
            var id = (string)entry["id"]!;
            var caseKey = caseToId.FirstOrDefault(p => p.Value == id).Key ?? "default";
            List<string?> found = special.TryGetValue(id, out var own) && own.Count > 0
                ? own
                : [byCase.GetValueOrDefault(caseKey) ?? byCase.GetValueOrDefault("default")];
            if (found.Any(string.IsNullOrEmpty))
            {
                errors.Add($"{cls}.{id}: no editor layout found in {factory}");
                continue;
            }
            layouts[id] = [.. found.Select(l => l!).Distinct().Order(StringComparer.Ordinal)];
        }
        return layouts;
    }

    [GeneratedRegex(@"Fragment editor\(\) \{(.*?)\n    \}", RegexOptions.Singleline)]
    private static partial Regex EditorBody();

    [GeneratedRegex(@"!= (\d+) \? [^:]+ : new (\w+)\(\)")]
    private static partial Regex OwnFragmentTernary();

    [GeneratedRegex(@"if \(\w+ != (\d+)\) \{.*?\}\s*return new (\w+)\(\)", RegexOptions.Singleline)]
    private static partial Regex OwnFragmentIf();

    [GeneratedRegex(@"(\w+)\.c\(inflater, container, false\)")]
    private static partial Regex ViewBinding();

    [GeneratedRegex(@"R\.layout\.\w+")]
    private static partial Regex LayoutRef();

    [GeneratedRegex(@"(\w+)\.INSTANCE\.")]
    private static partial Regex FactoryCall();

    [GeneratedRegex(@"^case (\d+):")]
    private static partial Regex Case();

    [GeneratedRegex(@"if \(\w+ [=!]= (\d+)\)")]
    private static partial Regex IfCase();

    [GeneratedRegex(@"== (\d+) \? R\.layout\.(\w+) : R\.layout\.(\w+)")]
    private static partial Regex TernaryLayouts();

    [GeneratedRegex(@"\w+ = R\.layout\.(\w+);")]
    private static partial Regex LayoutAssignment();

    [GeneratedRegex(@"\(getLayoutInflater\(\), R\.layout\.(\w+),")]
    private static partial Regex InflateCall();
}
