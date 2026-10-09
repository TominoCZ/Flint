namespace Flint.App.Setup;

internal enum StepState
{
    Waiting,
    Running,

    /// <summary>Setup cannot go on until the user does something (the detail says what); it checks again by itself.</summary>
    NeedsYou,

    Done,
    Failed,
}

/// <summary>One line of the setup screen.</summary>
/// <param name="Progress">0 to 1 while there is a measure of it; null for none.</param>
/// <param name="Command">A command for the user to run in a terminal, shown so it can be copied.</param>
internal sealed record SetupStep(string Title, StepState State = StepState.Waiting, string Detail = "", double? Progress = null, string? Command = null);
