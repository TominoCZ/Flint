using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Flint.App.Setup;

/// <summary>
/// Which parameter each knob, slider and switch of a pedal panel turns, read from its generated data binding, as the
/// app wires it: DataBinderMapperImpl names the binding class for "layout/&lt;name&gt;_0"; its constructor hands the
/// views to the base class in field order (objArr[N] is the view tagged binding_N, or the id sViewsWithIds gives N);
/// and each two-way listener reads one field (D6.h.m a knob, D6.h.r a slider, isChecked a switch) and writes
/// q.N(params, i, …).
/// </summary>
internal static partial class PanelBindings
{
    /// <summary>{"id:&lt;id&gt;" or "tag:binding_N": {"param": i, "kind": …}}, or null for a layout with no data binding.</summary>
    public static JsonObject? Controls(string layout, JavaSources sources, ResourceNames names)
    {
        var mapper = sources.Read("com/positivegrid/spark/DataBinderMapperImpl.java");
        var m = Regex.Match(mapper, "\"layout/" + layout + @"_0""\.equals\(obj\)\) \{\s*return new ([\w.]+)\(");
        if (!m.Success) return null;
        var cls = m.Groups[1].Value.Split('.')[^1];
        // One-letter class names repeat across packages: follow the mapper's own import.
        var qualified = m.Groups[1].Value.Contains('.') ? m.Groups[1].Value : Regex.Match(mapper, $@"import ([\w.]+\.{cls});").Groups[1].Value;
        var path = qualified.Replace('.', '/') + ".java";
        var impl = sources.Read(path);
        var indices = ViewIndex().Matches(SuperCall().Match(impl).Groups[1].Value).Select(i => int.Parse(i.Groups[1].Value)).ToList();
        var baseName = Regex.Match(impl, $@"class {cls} extends (\w+)").Groups[1].Value;
        var baseCode = sources.Read(Path.Combine(Path.GetDirectoryName(path)!, baseName + ".java"));
        var ctor = Regex.Match(baseCode, "protected " + baseName + @"\(Object \w+, View \w+, int \w+, (.*?)\) \{(.*?)\n    \}", RegexOptions.Singleline);
        var parameters = ctor.Groups[1].Value.Split(',').Select(p => p.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[^1]).ToList();
        var fieldOf = new Dictionary<string, string>();
        foreach (Match a in Assignment().Matches(ctor.Groups[2].Value)) fieldOf[a.Groups[2].Value] = a.Groups[1].Value;
        var viewOfIndex = new Dictionary<int, string>();
        foreach (Match v in ViewWithId().Matches(impl)) viewOfIndex[int.Parse(v.Groups[2].Value)] = "id:" + names.Real("id", v.Groups[1].Value);
        var viewOfField = new Dictionary<string, string>();
        for (var position = 0; position < parameters.Count; position++)
        {
            if (!fieldOf.TryGetValue(parameters[position], out var field) || position >= indices.Count) continue;
            var n = indices[position];
            viewOfField[field] = viewOfIndex.GetValueOrDefault(n, $"tag:binding_{n}");
        }

        var controls = new JsonObject();
        foreach (var block in InnerClass().Split(impl).Skip(1))
        {
            var fields = Regex.Matches(block, cls + @"\.this\.(\w+)");
            var write = ParamWrite().Match(block);
            if (fields.Count == 0 || !write.Success) continue;
            var kind = block.Contains("D6.h.m(") ? "knob" : block.Contains("D6.h.r(") ? "slider" : block.Contains("isChecked()") ? "switch" : "other";
            if (viewOfField.GetValueOrDefault(fields[0].Groups[1].Value) is { } view)
                controls[view] = new JsonObject { ["param"] = int.Parse(write.Groups[1].Value), ["kind"] = kind };
        }
        return controls;
    }

    [GeneratedRegex(@"super\(\w+, view, \d+, (.*?)\);", RegexOptions.Singleline)]
    private static partial Regex SuperCall();

    [GeneratedRegex(@"objArr\[(\d+)\]")]
    private static partial Regex ViewIndex();

    [GeneratedRegex(@"this\.(\w+) = (\w+);")]
    private static partial Regex Assignment();

    [GeneratedRegex(@"put\(R\.id\.(\w+), (\d+)\)")]
    private static partial Regex ViewWithId();

    [GeneratedRegex(@"\n    (?:final |private |public )*class ")]
    private static partial Regex InnerClass();

    [GeneratedRegex(@"q\.N\(\w+, (\d+),")]
    private static partial Regex ParamWrite();
}
