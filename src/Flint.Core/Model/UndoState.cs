namespace Flint.Core.Model;

/// <summary>The looper's undo/redo button: off until a dub ends, then undo and redo in turn.</summary>
public enum UndoState
{
    Unavailable,
    Undo,
    Redo,
}
