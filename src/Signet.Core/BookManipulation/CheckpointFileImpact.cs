namespace Signet.Core.BookManipulation;

/// <summary>What moving to another checkpoint state does to a file (<see cref="CheckpointFileImpact"/>).</summary>
public enum CheckpointFileChange
{
    /// <summary>The file exists now and disappears.</summary>
    Removed,

    /// <summary>The file does not exist now and comes back.</summary>
    Restored,

    /// <summary>The file gets back its name from the target state (same content).</summary>
    Renamed,
}

/// <summary>
/// A file that moving to another checkpoint state removes, brings back or renames
/// (<see cref="CheckpointHistory.UndoImpact"/>, <see cref="CheckpointHistory.RedoImpact"/>).
/// </summary>
/// <param name="Change">What happens to the file.</param>
/// <param name="BookPath">The file's bookpath now (for <see cref="CheckpointFileChange.Restored"/>: in the target state).</param>
/// <param name="NewBookPath">For <see cref="CheckpointFileChange.Renamed"/>: the bookpath in the target state; otherwise <c>null</c>.</param>
/// <param name="LosesEdits">
/// For <see cref="CheckpointFileChange.Removed"/>: the file was edited in an editor within the span the move undoes
/// (since the previous state for Undo, since the current state was reached for Redo), so those edits are lost;
/// otherwise <c>false</c>.
/// </param>
public sealed record CheckpointFileImpact(CheckpointFileChange Change, string BookPath, string? NewBookPath, bool LosesEdits);
