namespace Flint.Core.Protocol.Payloads;

/// <summary>Which preset buffer a slot number refers to: the app's <c>JamUpConst.PresetType</c>.</summary>
public enum PresetType
{
    /// <summary>A stored hardware slot, 0–7 on Spark 2 (bank B is 4–7).</summary>
    Slot = 0,
    /// <summary>The live edit buffer: what the amp is playing right now, unsaved edits included.</summary>
    Editing = 1,
    AutoTone = 2,
    Slot2 = 3,
    Editing2 = 4,
}
