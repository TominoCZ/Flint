using System.Text.Json;
using Flint.Core.Library;
using Flint.Core.Logging;

namespace Flint.App;

/// <summary><c>settings.json</c> next to the library: the amp to connect to straight away on every later run.</summary>
internal sealed record AppSettings(string? Address)
{
    private static readonly AppSettings None = new((string?)null);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static string FilePath => Path.Combine(PresetLibrary.DefaultRoot, "settings.json");

    /// <summary>The saved settings, or none. A broken file only costs one scan, so it is logged and ignored.</summary>
    public static AppSettings Load()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) ?? None : None;
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            Log.Warn("app", $"settings.json unreadable: {e.Message}");
            return None;
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(PresetLibrary.DefaultRoot);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
    }
}
