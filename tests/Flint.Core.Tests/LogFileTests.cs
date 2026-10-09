using Flint.Core.Logging;

namespace Flint.Core.Tests;

public sealed class LogFileTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("flint-log-").FullName;
    private DateTime _now = new(2026, 10, 7, 20, 5, 27, 543);

    [Fact]
    public void Writes_one_short_line_per_event_to_the_days_file()
    {
        var log = new LogFile(_dir, () => _now);
        log.Write(LogLevel.Info, "ble", "connected to Spark 2");
        log.Write(LogLevel.Warn, "amp", "no reply to 02 71");

        var lines = File.ReadAllLines(Path.Combine(_dir, "flint-2026-10-07.log"));
        Assert.Equal(["20:05:27.543 INF ble   connected to Spark 2", "20:05:27.543 WRN amp   no reply to 02 71"], lines);
    }

    [Fact]
    public void Rolls_over_to_a_new_file_at_midnight()
    {
        var log = new LogFile(_dir, () => _now);
        log.Write(LogLevel.Info, "app", "before");
        _now = _now.AddDays(1);
        log.Write(LogLevel.Info, "app", "after");

        Assert.True(File.Exists(Path.Combine(_dir, "flint-2026-10-08.log")));
    }

    [Fact]
    public void Prune_deletes_only_this_apps_files_older_than_two_weeks()
    {
        string[] old = ["flint-2026-09-22.log"], kept = ["flint-2026-09-23.log", "flint-2026-10-07.log", "notes.log", "flint-oops.log"];
        foreach (var name in old.Concat(kept)) File.WriteAllText(Path.Combine(_dir, name), "");

        new LogFile(_dir, () => _now).Prune();

        Assert.Equal(kept.Order(), Directory.GetFiles(_dir).Select(Path.GetFileName).Order());
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
