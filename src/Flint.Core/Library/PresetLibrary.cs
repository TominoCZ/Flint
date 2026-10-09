using System.Text.Json;
using Flint.Core.Logging;
using Flint.Core.Presets;

namespace Flint.Core.Library;

/// <summary>
/// The presets on the PC, which are the source of truth: one file per preset in the official
/// app's JSON under <c>presets/</c>, named after the preset, and the slot map in <c>slots.json</c>. Only the owner's own
/// <see cref="Save"/> replaces a preset file, and the old one goes to <c>presets/.history/</c> first. Every write goes to
/// a temporary file first, so a crash leaves the old file whole.
/// </summary>
public sealed class PresetLibrary(string root)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly char[] Unsafe = ['<', '>', ':', '"', '/', '\\', '|', '?', '*']; // Windows', so the folder copies anywhere

    /// <summary>
    /// <c>~/.local/share/Flint</c> on Linux, <c>%LOCALAPPDATA%\Flint</c> on Windows. Create: without it, a
    /// data folder that does not exist yet comes back as "", and this as a path relative to wherever the app started.
    /// </summary>
    public static string DefaultRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create), "Flint");

    /// <summary>How many earlier versions of a preset <see cref="Save"/> keeps.</summary>
    public const int HistoryDepth = 10;

    public string PresetsDirectory => Path.Combine(root, "presets");

    public string HistoryDirectory => Path.Combine(PresetsDirectory, ".history");

    private string SlotMapPath => Path.Combine(root, "slots.json");

    /// <summary>Throws <see cref="IOException"/> if the file is gone, <see cref="FormatException"/> if it is not a preset.</summary>
    public Preset Load(string entry) => PresetJson.Read(File.ReadAllText(PathOf(entry)));

    /// <summary>Writes the preset as a new entry and returns its name. A name already taken gets " (2)", " (3)" and so on.</summary>
    public string Add(Preset preset)
    {
        Directory.CreateDirectory(PresetsDirectory);
        var temp = WriteTemp(PresetsDirectory, PresetJson.Write(preset));
        var stem = FileStem(preset.Name);
        for (var n = 1; ; n++)
        {
            var entry = n == 1 ? stem : $"{stem} ({n})";
            try
            {
                File.Move(temp, PathOf(entry), overwrite: false);
                return entry;
            }
            catch (IOException) when (File.Exists(PathOf(entry)))
            {
            }
        }
    }

    /// <summary>
    /// Adds a preset file from elsewhere (the phone app, ToneCloud, a backup) as a new entry and returns its name. An id
    /// the library already holds gets a fresh one, so that each id names one entry (<see cref="Find"/>). Nothing is
    /// overwritten. Throws <see cref="FormatException"/> if the text is not a preset.
    /// </summary>
    public string Import(string json)
    {
        var preset = PresetJson.Read(json);
        return Add(Find(preset.Id) is null ? preset : preset with { Id = Guid.NewGuid().ToString() });
    }

    /// <summary>
    /// Overwrites an entry with the owner's save. The file it replaces goes to <c>.history/&lt;entry&gt;/</c> first, and only
    /// the last <see cref="HistoryDepth"/> versions are kept there, so a bad save can be undone by hand.
    /// </summary>
    public void Save(string entry, Preset preset)
    {
        var path = PathOf(entry);
        var temp = WriteTemp(PresetsDirectory, PresetJson.Write(preset));
        if (File.Exists(path))
        {
            var history = Directory.CreateDirectory(Path.Combine(HistoryDirectory, entry));
            File.Copy(path, Path.Combine(history.FullName, $"{DateTime.Now:yyyyMMdd-HHmmss-fffffff}.json"), overwrite: true);
            foreach (var old in history.GetFiles("*.json").OrderByDescending(f => f.Name).Skip(HistoryDepth)) old.Delete();
        }
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Every preset in the library by entry name, in name order. A file that is not a preset is logged and skipped.</summary>
    public IEnumerable<(string Entry, Preset Preset)> Entries()
    {
        if (!Directory.Exists(PresetsDirectory)) yield break;
        foreach (var path in Directory.GetFiles(PresetsDirectory, "*.json").Order(StringComparer.OrdinalIgnoreCase))
        {
            var entry = Path.GetFileNameWithoutExtension(path);
            Preset preset;
            try
            {
                preset = Load(entry);
            }
            catch (Exception e) when (e is IOException or FormatException)
            {
                Log.Warn("lib", $"{entry}: {e.Message}; skipped");
                continue;
            }
            yield return (entry, preset);
        }
    }

    /// <summary>The entry holding the preset with this id, or null.</summary>
    // ponytail: reads every file per lookup; fine for a few hundred presets, index by id if the library grows past that.
    public string? Find(string id) => Entries().FirstOrDefault(e => e.Preset.Id == id).Entry;

    /// <summary>The slot map, or an empty one. A file that cannot be read is set aside, not overwritten, and logged.</summary>
    public SlotMap LoadSlotMap()
    {
        if (!File.Exists(SlotMapPath)) return SlotMap.Empty;
        try
        {
            return (JsonSerializer.Deserialize<SlotMap>(File.ReadAllText(SlotMapPath), Json) ?? SlotMap.Empty).Normalised();
        }
        catch (JsonException e)
        {
            var aside = $"{SlotMapPath}.bad-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Move(SlotMapPath, aside);
            Log.Warn("lib", $"slots.json unreadable ({e.Message}), moved to {Path.GetFileName(aside)}");
            return SlotMap.Empty;
        }
    }

    public void SaveSlotMap(SlotMap map)
    {
        Directory.CreateDirectory(root);
        File.Move(WriteTemp(root, JsonSerializer.Serialize(map, Json)), SlotMapPath, overwrite: true);
    }

    private string PathOf(string entry) =>
        Path.GetFileName(entry) == entry && entry.Length > 0
            ? Path.Combine(PresetsDirectory, entry + ".json")
            : throw new FormatException($"\"{entry}\" is not a preset entry name");

    private static string WriteTemp(string directory, string text)
    {
        var temp = Path.Combine(directory, $".{Guid.NewGuid():N}.tmp");
        File.WriteAllText(temp, text);
        return temp;
    }

    /// <summary>The preset's name as a file name on any OS: no reserved characters, no leading dot, no trailing dot or space.</summary>
    public static string FileStem(string name)
    {
        var stem = new string([.. name.Select(c => char.IsControl(c) || Unsafe.Contains(c) ? '_' : c)]).Trim().TrimStart('.').TrimEnd('.', ' ');
        return stem.Length > 0 ? stem : "Preset";
    }
}
