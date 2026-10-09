using Avalonia.Platform.Storage;
using Flint.Core.Library;
using Flint.Core.Logging;
using Flint.Core.Presets;

namespace Flint.App.Tone;

/// <summary>
/// The ⋮ menu's Export and Import: presets as files in the app's own JSON (<see cref="PresetJson"/>),
/// through the desktop's file picker, so they go to and from the phone app, ToneCloud downloads and backups as they are.
/// </summary>
internal static class PresetFiles
{
    private const long MaxBytes = 1 << 20; // a preset is a few KB; anything this big is not one

    private static readonly FilePickerFileType Json = new("Spark preset") { Patterns = ["*.json"], MimeTypes = ["application/json"] };

    /// <summary>Writes the preset where the owner picks; false if they cancelled.</summary>
    public static async Task<bool> ExportAsync(IStorageProvider storage, Preset preset)
    {
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export preset", SuggestedFileName = PresetLibrary.FileStem(preset.Name) + ".json", DefaultExtension = "json",
            FileTypeChoices = [Json], ShowOverwritePrompt = true,
        });
        if (file is null) return false;
        await using var stream = await file.OpenWriteAsync();
        if (stream.CanSeek) stream.SetLength(0); // replacing a longer file must not leave its tail
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(PresetJson.Write(preset));
        Log.Info("lib", $"exported \"{preset.Name}\" to {file.Name}");
        return true;
    }

    /// <summary>
    /// Imports the files the owner picks through <paramref name="import"/>, and returns how many went in and the names of
    /// those that are not presets (logged with the reason). Files are untrusted: <see cref="PresetJson"/> checks each.
    /// </summary>
    public static async Task<(int Imported, IReadOnlyList<string> Rejected)> ImportAsync(IStorageProvider storage, Func<string, string> import)
    {
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Import presets", AllowMultiple = true, FileTypeFilter = [Json] });
        var (imported, rejected) = (0, new List<string>());
        foreach (var file in files)
        {
            try
            {
                await using var stream = await file.OpenReadAsync();
                if (stream.CanSeek && stream.Length > MaxBytes) throw new FormatException("too big for a preset");
                using var reader = new StreamReader(stream);
                import(await reader.ReadToEndAsync());
                imported++;
            }
            catch (FormatException e)
            {
                Log.Warn("lib", $"{file.Name} not imported: {e.Message}");
                rejected.Add(file.Name);
            }
        }
        return (imported, rejected);
    }
}
