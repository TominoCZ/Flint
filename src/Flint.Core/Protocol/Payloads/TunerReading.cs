namespace Flint.Core.Protocol.Payloads;

/// <summary>
/// CTunerNotify (<c>03 64</c>, about every 60 ms while the tuner is on): the note, 0 C … 11 B, and how far off it is,
/// 0 to 1 with 0.5 in tune; −1.0 means no signal (the app's TunerSourceImpl and TunerViewModel).
/// </summary>
public sealed record TunerReading(uint Note, float Offset) : IPayload
{
    private static readonly string[] Names = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    public bool HasSignal => Offset != -1.0f;

    /// <summary>The note as the app writes it ("C#"), or "" for an index it has no name for.</summary>
    public string Name => Note < Names.Length ? Names[Note] : "";

    /// <summary>The app's green band: 0.46 to 0.54.</summary>
    public bool InTune => HasSignal && Offset is >= 0.46f and <= 0.54f;

    public static TunerReading Decode(byte[] payload)
    {
        var reader = new MsgPackReader(payload);
        return new(reader.ReadUInt(), reader.ReadFloat());
    }

    public byte[] Encode() => new MsgPackWriter().UInt(Note).Float(Offset).ToArray();
}
