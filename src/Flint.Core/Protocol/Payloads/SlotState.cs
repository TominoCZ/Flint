namespace Flint.Core.Protocol.Payloads;

/// <summary>
/// One entry of <see cref="SlotStates"/> (the app's <c>PresetSlotState</c>). <see cref="Slot"/> is the current slot
/// while <see cref="Edited"/> is set; the connect-time reply had 0x17CDB2D7 there with Edited false.
/// </summary>
public sealed record SlotState(int SignalChain, uint Slot, bool Edited);
