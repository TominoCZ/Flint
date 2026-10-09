namespace Flint.Core.Protocol.Payloads;

/// <summary>
/// CPresetSlotStates (<c>03 1a</c>): the reply to <c>02 1a</c>, also sent unasked after the amp's own knob is turned
/// to mark the slot edited. The array size counts entries, but each entry is three flat values.
/// </summary>
public sealed record SlotStates(IReadOnlyList<SlotState> States) : IPayload
{
    public static SlotStates Decode(byte[] payload)
    {
        var reader = new MsgPackReader(payload);
        return new([.. Enumerable.Range(0, reader.ReadArrayHeader())
            .Select(_ => new SlotState(reader.ReadInt(), reader.ReadUInt(), reader.ReadBool()))]);
    }

    public byte[] Encode()
    {
        var writer = new MsgPackWriter().ArrayHeader(States.Count);
        foreach (var state in States) writer.Int(state.SignalChain).UInt(state.Slot).Bool(state.Edited);
        return writer.ToArray();
    }
}
