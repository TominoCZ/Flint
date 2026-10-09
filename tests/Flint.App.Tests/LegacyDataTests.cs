namespace Flint.App.Tests;

// Flint was SparkControl: a user's presets, settings and artwork come along on the first start after the rename,
// even when an install already made Flint's folder, and the login entry follows.
public sealed class LegacyDataTests : IDisposable
{
    private readonly string _home = Directory.CreateTempSubdirectory("flint-legacy-").FullName;

    public void Dispose() => Directory.Delete(_home, recursive: true);

    [Fact]
    public void The_old_folder_moves_into_Flints_and_the_entries_follow()
    {
        var (local, config) = (Path.Combine(_home, "share"), Path.Combine(_home, "config"));
        var (old, root) = (Path.Combine(local, "SparkControl"), Path.Combine(local, "Flint"));
        Write(Path.Combine(old, "presets", "mine.json"), "{}");
        Write(Path.Combine(old, "settings.json"), "{\"address\":\"x\"}");
        Write(Path.Combine(old, "assets", "manifest.json"), "old");
        Write(Path.Combine(root, "assets", "manifest.json"), "installed"); // build-linux.sh --install got there first
        Write(Path.Combine(local, "applications", "SparkControl.desktop"), "[Desktop Entry]");
        Write(Path.Combine(config, "autostart", "SparkControl.desktop"), "[Desktop Entry]");

        var said = LegacyData.Move(local, config, root);

        Assert.True(File.Exists(Path.Combine(root, "presets", "mine.json")));
        Assert.True(File.Exists(Path.Combine(root, "settings.json")));
        Assert.Equal("installed", File.ReadAllText(Path.Combine(root, "assets", "manifest.json")));
        Assert.True(File.Exists(Path.Combine(old, "assets", "manifest.json"))); // kept, not overwritten or lost
        Assert.Contains("assets", said);
        Assert.False(File.Exists(Path.Combine(local, "applications", "SparkControl.desktop")));
        Assert.True(File.Exists(Path.Combine(config, "autostart", DesktopEntry.FileName)));
        Assert.Null(LegacyData.Move(Path.Combine(_home, "nothing"), config, root)); // nothing to move
    }

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}
