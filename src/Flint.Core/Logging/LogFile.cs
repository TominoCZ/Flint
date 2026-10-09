namespace Flint.Core.Logging;

public enum LogLevel { Info, Warn, Error }

/// <summary>
/// A day-per-file text log. One short line per event: <c>20:05:27.543 INF ble   connected to Spark 2</c>, i.e.
/// time, level, area (a short subsystem name), message. Files older than <see cref="KeepDays"/> are deleted.
/// </summary>
public sealed class LogFile
{
    public const int KeepDays = 14;
    private const string Prefix = "flint-";

    private readonly Lock _lock = new();
    private readonly Func<DateTime> _now;
    private StreamWriter? _writer;
    private DateOnly _day;

    public LogFile(string directory, Func<DateTime>? now = null)
    {
        Directory = directory;
        _now = now ?? (() => DateTime.Now);
    }

    public string Directory { get; }

    /// <summary>Mirror every line to stdout as well, for running from a terminal.</summary>
    public bool Echo { get; init; }

    public void Write(LogLevel level, string area, string message)
    {
        var now = _now();
        var line = $"{now:HH:mm:ss.fff} {Tag(level)} {area,-5} {message}";
        lock (_lock)
        {
            try
            {
                WriterFor(DateOnly.FromDateTime(now)).WriteLine(line);
            }
            catch (IOException)
            {
                // A full or read-only disk must never take the app down with it. The echo below still shows the line.
            }
            if (Echo) Console.WriteLine(line);
        }
    }

    /// <summary>Deletes this app's log files older than <see cref="KeepDays"/>. Touches nothing else in the directory.</summary>
    public void Prune()
    {
        var cutoff = DateOnly.FromDateTime(_now()).AddDays(-KeepDays);
        foreach (var file in System.IO.Directory.EnumerateFiles(Directory, Prefix + "*.log"))
            if (DateOnly.TryParseExact(Path.GetFileNameWithoutExtension(file)[Prefix.Length..], "yyyy-MM-dd", out var day) && day < cutoff)
                File.Delete(file);
    }

    public static string FileName(DateOnly day) => $"{Prefix}{day:yyyy-MM-dd}.log";

    private StreamWriter WriterFor(DateOnly day)
    {
        if (_writer is not null && day == _day) return _writer;
        _writer?.Dispose();
        System.IO.Directory.CreateDirectory(Directory);
        _day = day;
        return _writer = new StreamWriter(Path.Combine(Directory, FileName(day)), append: true) { AutoFlush = true };
    }

    private static string Tag(LogLevel level) => level switch
    {
        LogLevel.Info => "INF",
        LogLevel.Warn => "WRN",
        _ => "ERR",
    };
}
