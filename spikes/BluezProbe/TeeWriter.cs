using System.Text;

namespace BluezProbe;

/// <summary>Writes everything to the console and to a file, flushed per write so the file can be read while it grows.</summary>
internal sealed class TeeWriter(TextWriter console, StreamWriter file) : TextWriter
{
    public override Encoding Encoding => console.Encoding;

    public override void Write(char value)
    {
        console.Write(value);
        file.Write(value);
    }

    public override void Write(string? value)
    {
        console.Write(value);
        file.Write(value);
    }

    public override void Flush()
    {
        console.Flush();
        file.Flush();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) file.Dispose();
        base.Dispose(disposing);
    }
}
