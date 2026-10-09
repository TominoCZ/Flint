namespace Flint.App.Setup;

/// <summary>
/// jadx's Java sources of the Spark app (<c>decompiled/sources</c>), read by path or found by file name. jadx ends lines
/// as the OS does, so on Windows with \r\n; they are read with \n, which the patterns that read them expect.
/// </summary>
internal sealed class JavaSources(string root)
{
    public string Root { get; } = root;

    /// <summary>A class of the app's catalog, in <c>com.positivegrid.spark.model</c>.</summary>
    public string Model(string cls) => Read($"com/positivegrid/spark/model/{cls}.java");

    public string Read(string relativePath) => Text(Path.Combine(Root, relativePath));

    /// <summary>A class by its full name (<c>a.b.C</c>).</summary>
    public string Class(string qualified) => Read(qualified.Replace('.', '/') + ".java");

    /// <summary>The first file of this name anywhere, optionally only in a folder of this name.</summary>
    public string Find(string fileName, string? folder = null) =>
        Text(Directory.EnumerateFiles(Root, fileName, SearchOption.AllDirectories)
            .First(p => folder is null || Path.GetFileName(Path.GetDirectoryName(p)) == folder));

    private static string Text(string path) => File.ReadAllText(path).ReplaceLineEndings("\n");

    /// <summary>A call's arguments, split at the commas outside parentheses.</summary>
    public static List<string> SplitArgs(string text)
    {
        var (args, depth, current) = (new List<string>(), 0, "");
        foreach (var ch in text)
        {
            if (ch == ',' && depth == 0)
            {
                args.Add(current.Trim());
                current = "";
                continue;
            }
            depth += ch == '(' ? 1 : ch == ')' ? -1 : 0;
            current += ch;
        }
        if (current.Trim().Length > 0) args.Add(current.Trim());
        return args;
    }
}
