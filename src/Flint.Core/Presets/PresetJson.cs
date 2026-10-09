using System.Text.Json;
using System.Text.Json.Nodes;

namespace Flint.Core.Presets;

/// <summary>
/// The official app's preset JSON (its <c>preset.json</c> files and what <c>uploadPreset</c> takes), which the local
/// library stores. Same keys and nesting as libpghw's convertCMpPresetToJsonPreset:
/// <c>type, bpm, [loudness, extraGain], meta {id, name, version, description, icon}, sigpath [{type, dspId, active,
/// params [{index, value}]}]</c>. A knob value is a number, or an array when it has several; factory presets also
/// use <c>true</c>/<c>false</c>, read as 1 and 0 as the native asFloat does.
/// </summary>
public static class PresetJson
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static string Write(Preset preset)
    {
        var json = new JsonObject { ["type"] = "jamup_speaker", ["bpm"] = preset.Bpm };
        if (preset.Loudness is not null || preset.ExtraGain is not null)
        {
            json["loudness"] = preset.Loudness ?? Preset.UnsetLoudness;
            json["extraGain"] = preset.ExtraGain ?? Preset.DefaultExtraGain;
        }
        json["meta"] = new JsonObject
        {
            ["id"] = preset.Id, ["name"] = preset.Name, ["version"] = preset.Version,
            ["description"] = preset.Description, ["icon"] = preset.Icon,
        };
        json["sigpath"] = new JsonArray([.. preset.Modules.Select(module => new JsonObject
        {
            ["type"] = "speaker_fx",
            ["dspId"] = module.DspId,
            ["active"] = module.Active,
            ["params"] = new JsonArray([.. module.Parameters.Select(p => new JsonObject
            {
                ["index"] = p.Index,
                ["value"] = p.Values.Count == 1 ? JsonValue.Create(p.Values[0]) : new JsonArray([.. p.Values.Select(v => JsonValue.Create(v))]),
            })]),
        })]);
        return json.ToJsonString(Indented);
    }

    /// <summary>Throws <see cref="FormatException"/> naming what is missing or malformed; imported files are untrusted.</summary>
    public static Preset Read(string text)
    {
        try
        {
            return Parse(JsonNode.Parse(text));
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException) // bad syntax, or a value of the wrong kind
        {
            throw new FormatException($"not a preset: {e.Message}", e);
        }
    }

    private static Preset Parse(JsonNode? root)
    {
        if (root is not JsonObject json) throw new FormatException("a preset must be a JSON object");

        var meta = json["meta"] as JsonObject ?? throw new FormatException("no \"meta\" object");
        var sigpath = json["sigpath"] as JsonArray ?? throw new FormatException("no \"sigpath\" array");
        return new Preset(
            Text(meta, "id"), Text(meta, "name"), Text(meta, "version"), Text(meta, "description"), Text(meta, "icon"),
            Number(json["bpm"] ?? throw new FormatException("no \"bpm\"")),
            [.. sigpath.Select(ReadModule)],
            json["loudness"] is { } loudness ? Number(loudness) : null,
            json["extraGain"] is { } extraGain ? Number(extraGain) : null);
    }

    /// <summary>One module on its own, in the sigpath entry's form (the app's <c>ModulePresets/…/data.json</c>).</summary>
    public static PresetModule ReadModule(string text)
    {
        try
        {
            return ReadModule(JsonNode.Parse(text));
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            throw new FormatException($"not a module: {e.Message}", e);
        }
    }

    private static PresetModule ReadModule(JsonNode? node)
    {
        var module = node as JsonObject ?? throw new FormatException("a sigpath entry is not an object");
        var dspId = Text(module, "dspId");
        var parameters = module["params"] as JsonArray ?? throw new FormatException($"{dspId}: no \"params\" array");
        return new PresetModule(dspId, module["active"]?.GetValue<bool>() ?? false, [.. parameters.Select(p =>
        {
            var index = (int)Number(p?["index"] ?? throw new FormatException($"{dspId}: a parameter has no \"index\""));
            var value = p["value"] ?? throw new FormatException($"{dspId}: parameter {index} has no \"value\"");
            return new PresetParameter(index, value is JsonArray values ? [.. values.Select(v => Number(v!))] : [Number(value)]);
        })]);
    }

    private static string Text(JsonObject json, string key) => json[key]?.GetValue<string>() ?? "";

    private static float Number(JsonNode node) => node.GetValueKind() switch
    {
        JsonValueKind.Number => node.GetValue<float>(),
        JsonValueKind.True => 1f,
        JsonValueKind.False => 0f,
        var kind => throw new FormatException($"expected a number, found {kind}"),
    };
}
