namespace Flint.Core.Logging;

/// <summary>
/// The app's log. Writes to <see cref="Directory"/>: <c>~/.local/share/Flint/logs</c> on Linux,
/// <c>%LOCALAPPDATA%\Flint\logs</c> on Windows, or <c>$FLINT_LOG_DIR</c> when set.
/// Areas are short lowercase names: <c>app</c>, <c>ble</c>, <c>amp</c>, <c>ui</c>.
/// </summary>
public static class Log
{
    private static LogFile _file = new(DefaultDirectory());

    public static string Directory => _file.Directory;

    /// <summary>Call once at startup. Prunes old files and decides whether lines are echoed to the terminal.</summary>
    public static void Start(bool echo)
    {
        _file = new LogFile(_file.Directory) { Echo = echo };
        try
        {
            System.IO.Directory.CreateDirectory(_file.Directory);
            _file.Prune();
        }
        catch (IOException)
        {
            // Old logs that cannot be deleted are not worth failing a launch over.
        }
    }

    public static void Info(string area, string message) => _file.Write(LogLevel.Info, area, message);

    public static void Warn(string area, string message) => _file.Write(LogLevel.Warn, area, message);

    /// <summary>One line with the exception's type and message; the stack trace follows, indented, for errors only.</summary>
    public static void Error(string area, string message, Exception? exception = null) =>
        _file.Write(LogLevel.Error, area, exception is null
            ? message
            : $"{message}: {exception.GetType().Name}: {exception.Message}\n    {exception.StackTrace?.Trim().Replace("\n", "\n    ")}");

    private static string DefaultDirectory() =>
        Environment.GetEnvironmentVariable("FLINT_LOG_DIR") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Flint", "logs");
}
