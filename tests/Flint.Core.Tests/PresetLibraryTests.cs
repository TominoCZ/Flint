using Flint.Core.Library;
using Flint.Core.Presets;

namespace Flint.Core.Tests;

// The owner's saves: an overwrite keeps the old version in .history/, the last 10 of them.
public sealed class PresetLibraryTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("flint-lib-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Saving_over_an_entry_keeps_the_last_10_versions_and_finds_it_by_id()
    {
        var library = new PresetLibrary(_root);
        var preset = FakeAmp.Sample("Crunch");
        var entry = library.Add(preset);
        for (var i = 1; i <= 12; i++) library.Save(entry, preset with { Name = $"Crunch {i}" });

        Assert.Equal("Crunch 12", library.Load(entry).Name);
        Assert.Equal(PresetLibrary.HistoryDepth, Directory.GetFiles(Path.Combine(library.HistoryDirectory, entry)).Length);
        Assert.Equal(entry, library.Find(preset.Id));
        Assert.Null(library.Find("no such id"));
        Assert.Single(library.Entries());
    }

    [Fact]
    public void Importing_never_overwrites_and_gives_a_repeated_id_a_fresh_one()
    {
        var library = new PresetLibrary(_root);
        var json = PresetJson.Write(FakeAmp.Sample("Crunch"));

        var first = library.Import(json);
        var second = library.Import(json);

        Assert.Equal(["Crunch", "Crunch (2)"], [first, second]);
        Assert.NotEqual(library.Load(first).Id, library.Load(second).Id);
        Assert.Throws<FormatException>(() => library.Import("{\"bpm\": 120}"));
        Assert.Equal(2, library.Entries().Count());
    }
}
