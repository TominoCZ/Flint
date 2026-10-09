using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Flint.App.Setup;

/// <summary>
/// Builds <c>assets/catalog.json</c>: every model the Spark app offers, per chain slot, with the images and panel
/// layout it uses. The app keeps this in code, not data, so it is read from jadx's decompile. The C# twin of
/// scripts/extract-catalog.py; ExtractionParityTests keeps the two in step.
///
/// Fails, naming what is wrong, if any model lacks a layout or any image or layout it names (directly, or through a
/// layout or XML drawable) is missing from the assets. That check is what keeps this parser honest when the app changes.
/// </summary>
internal static partial class CatalogBuilder
{
    // The chain, left to right, as the strip labels it, with its catalog classes and its ModulePresets folder.
    private static readonly (string Label, string[] Classes, string Folder)[] Chain =
    [
        ("NOISEGATE", ["FxNoiseGate"], "NoiseGate"), ("COMP/WAH", ["FxComp"], "Comp"), ("DRIVE", ["FxDrive"], "Drive"),
        ("AMP", ["FxAmp", "FxPreamp"], "Amp"), ("MOD/EQ", ["FxModulation"], "Modulation"), ("DELAY", ["FxDelay"], "Delay"),
        ("REVERB", ["FxReverb"], "Reverb"),
    ];

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>Writes catalog.json into <paramref name="assets"/> and returns a one-line summary. Throws <see cref="InvalidDataException"/> listing every problem.</summary>
    public static string Build(string decompiled, string assets)
    {
        var sources = new JavaSources(Path.Combine(decompiled, "sources"));
        var names = new ResourceNames(sources);
        var res = Path.Combine(assets, "res");
        var errors = new List<string>();

        var renamed = RawIds.Name(res, names);
        var reverbs = ModelEnums.ReverbTypes(sources, names, errors);
        var chain = new JsonArray();
        foreach (var (label, classes, folder) in Chain)
        {
            var models = new List<JsonObject>();
            foreach (var cls in classes)
            {
                var entries = ModelEnums.Entries(cls, sources, names, errors);
                var layouts = EditorLayouts.Read(cls, entries, sources, names, errors);
                foreach (var entry in entries)
                {
                    entry["editor"] = new JsonArray([.. layouts.GetValueOrDefault((string)entry["id"]!, []).Select(l => JsonValue.Create(l))]);
                    models.Add(entry);
                }
            }
            chain.Add(new JsonObject
            {
                ["label"] = label,
                ["models"] = new JsonArray([.. models]),
                ["order"] = PickerOrder(assets, folder, models, reverbs, errors),
            });
        }
        var catalog = new JsonObject
        {
            ["chain"] = chain,
            ["reverbTypes"] = new JsonArray([.. reverbs]),
            ["knobStyles"] = ModelEnums.KnobStyles(sources, names),
            ["panels"] = Panels(res, Editors(chain), sources, names, errors),
        };

        var checkedFiles = CheckFiles(res, catalog, errors);
        if (errors.Count > 0) throw new InvalidDataException($"catalog: {errors.Count} problem(s):\n  " + string.Join("\n  ", errors));
        File.WriteAllText(Path.Combine(assets, "catalog.json"), catalog.ToJsonString(Indented));
        var modelCount = chain.Sum(s => s!["models"]!.AsArray().Count);
        return $"catalog: {modelCount} models in {chain.Count} chain slots, {reverbs.Count} reverb types, " +
               $"{catalog["knobStyles"]!.AsObject().Count} knob styles, {checkedFiles} files checked, bare ids named in {renamed} files";
    }

    /// <summary>
    /// The model sheet's order, from ModulePresets/&lt;folder&gt;/order.json. Each entry's folder also holds the knob
    /// values the app loads when that model is picked (data.json). A reverb entry is a type of bias.reverb, told by its param 6.
    /// </summary>
    private static JsonArray PickerOrder(string assets, string folder, List<JsonObject> models, List<JsonObject> reverbs, List<string> errors)
    {
        var presets = Path.Combine(assets, "ModulePresets", folder);
        var order = new JsonArray();
        foreach (var module in JsonNode.Parse(File.ReadAllText(Path.Combine(presets, "order.json")))!["modules"]!.AsArray())
        {
            var path = (string)module!["path"]!;
            var data = JsonNode.Parse(File.ReadAllText(Path.Combine(presets, path, "data.json")))!;
            var dspId = (string)data["dspId"]!;
            var entry = new JsonObject { ["preset"] = $"ModulePresets/{folder}/{path}/data.json", ["dspId"] = dspId };
            if (dspId == "bias.reverb")
            {
                var value = data["params"]!.AsArray().First(p => (int)p!["index"]! == 6)!["value"]!.GetValue<double>();
                var type = Math.Round(value, 1, MidpointRounding.ToEven);
                entry["reverbType"] = type;
                if (!reverbs.Any(t => (double)t["value"]! == type)) errors.Add($"{folder}/{path}: no reverb type {value}");
            }
            else if (!models.Any(m => (string?)m["dspId"] == dspId))
            {
                errors.Add($"{folder}/{path}: no model with dspId {dspId}");
            }
            order.Add(entry);
        }
        return order;
    }

    private static IEnumerable<string> Editors(JsonArray chain) =>
        chain.SelectMany(s => s!["models"]!.AsArray()).SelectMany(m => m!["editor"]!.AsArray()).Select(e => (string)e!);

    /// <summary>Each pedal panel's controls; every knob and slider in it must turn some parameter.</summary>
    private static JsonObject Panels(string res, IEnumerable<string> editors, JavaSources sources, ResourceNames names, List<string> errors)
    {
        var panels = new JsonObject();
        foreach (var name in editors.Where(e => !e.StartsWith("editor_amp")).Distinct().Order(StringComparer.Ordinal))
        {
            var controls = PanelBindings.Controls(name, sources, names);
            if (controls is null)
            {
                // The two wahs use view bindings driven from their fragments' code, not data binding. The Auto Wah's one
                // knob is param 4 (AutoWahFragment's listener: hVar.n().put(4, value)). The J.H. Legendary Wah's phone
                // panel has no controls of its own: they are in a dialog its mode button opens, not drawn here.
                if (name.StartsWith("editor_auto_wah"))
                {
                    controls = new JsonObject { ["id:sensitivity_knob"] = new JsonObject { ["param"] = 4, ["kind"] = "knob" } };
                }
                else
                {
                    if (!name.Contains("wah")) errors.Add($"{name}: no data binding found");
                    continue;
                }
            }
            var layout = File.ReadAllText(Path.Combine(res, "layout", name + ".xml"));
            foreach (Match control in TurningControl().Matches(layout))
            {
                var body = control.Groups[2].Value;
                var keys = new[] { ViewId().Match(body), ViewTag().Match(body) }
                    .Zip(["id:", "tag:"], (m, prefix) => m.Success ? prefix + m.Groups[1].Value : null).OfType<string>().ToList();
                if (!keys.Any(controls.ContainsKey))
                    errors.Add($"{name}: {control.Groups[1].Value.Split('.')[^1]} [{string.Join(", ", keys)}] turns no parameter");
            }
            panels[name] = controls;
        }
        return panels;
    }

    /// <summary>Every image and layout the catalog names, and every drawable those name in turn, must exist. Returns how many were checked.</summary>
    private static int CheckFiles(string res, JsonObject catalog, List<string> errors)
    {
        var todo = new Stack<(string Kind, string? Name)>();
        foreach (var model in catalog["chain"]!.AsArray().SelectMany(s => s!["models"]!.AsArray()))
        {
            foreach (var key in new[] { "icon", "itemIcon", "face", "cab" }) todo.Push(("drawable", (string?)model![key]));
            foreach (var layout in model!["editor"]!.AsArray()) todo.Push(("layout", (string?)layout));
        }
        foreach (var type in catalog["reverbTypes"]!.AsArray())
            foreach (var key in new[] { "face", "icon", "itemIcon" }) todo.Push(("drawable", (string?)type![key]));
        foreach (var (_, drawable) in catalog["knobStyles"]!.AsObject()) todo.Push(("drawable", (string?)drawable));

        var seen = new HashSet<(string, string)>();
        while (todo.TryPop(out var item))
        {
            if (string.IsNullOrEmpty(item.Name) || !seen.Add((item.Kind, item.Name))) continue;
            var path = item.Kind == "drawable"
                ? Directory.EnumerateDirectories(res, "drawable*").SelectMany(d => Directory.EnumerateFiles(d, item.Name + ".*")).FirstOrDefault()
                : Directory.EnumerateDirectories(res, "layout*").Select(d => Path.Combine(d, item.Name + ".xml")).FirstOrDefault(File.Exists);
            if (path is null)
                errors.Add($"missing {item.Kind} {item.Name}");
            else if (path.EndsWith(".xml"))
                foreach (Match d in DrawableRef().Matches(File.ReadAllText(path))) todo.Push(("drawable", d.Groups[1].Value));
        }
        return seen.Count;
    }

    [GeneratedRegex(@"<([\w.]+\.(?:Knob|Slider))\b(.*?)/?>", RegexOptions.Singleline)]
    private static partial Regex TurningControl();

    [GeneratedRegex(@"android:id=""@\+id/(\w+)""")]
    private static partial Regex ViewId();

    [GeneratedRegex(@"android:tag=""(binding_\d+)""")]
    private static partial Regex ViewTag();

    [GeneratedRegex(@"@drawable/(\w+)")]
    private static partial Regex DrawableRef();
}
