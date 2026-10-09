using System.Diagnostics;
using Flint.Core.Logging;

namespace Flint.App.Setup;

/// <summary>Runs a tool (adb, java, jadx) to its end, handing each line it prints to the caller as it comes.</summary>
internal static class CommandLine
{
    /// <summary>The exit code and everything printed, stdout and stderr together. Cancelling kills the tool and its children.</summary>
    public static async Task<(int ExitCode, string Output)> RunAsync(string file, IEnumerable<string> args, CancellationToken cancel,
                                                                     Action<string>? line = null, IDictionary<string, string>? environment = null)
    {
        var start = new ProcessStartInfo(file, args) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var (key, value) in environment ?? new Dictionary<string, string>()) start.Environment[key] = value;
        using var process = Process.Start(start) ?? throw new IOException($"{file} did not start");
        var output = new System.Text.StringBuilder();
        void Read(string? text)
        {
            if (text is null) return;
            lock (output) output.AppendLine(text);
            line?.Invoke(text);
        }
        process.OutputDataReceived += (_, e) => Read(e.Data);
        process.ErrorDataReceived += (_, e) => Read(e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(cancel);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
        lock (output) return (process.ExitCode, output.ToString());
    }

    /// <summary>The tool's full path when it is on PATH, or null. On Windows, <c>adb</c> is <c>adb.exe</c>.</summary>
    public static string? OnPath(string tool) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(dir => Path.Combine(dir, Exe(tool))).FirstOrDefault(File.Exists);

    /// <summary>A program's file name on this OS.</summary>
    public static string Exe(string name) => OperatingSystem.IsWindows() ? name + ".exe" : name;

    /// <summary>The last few lines of a tool's output, for an error message and the log.</summary>
    public static string Tail(string output, int lines = 3)
    {
        var tail = string.Join(" / ", output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).TakeLast(lines));
        Log.Warn("setup", tail);
        return tail;
    }
}
