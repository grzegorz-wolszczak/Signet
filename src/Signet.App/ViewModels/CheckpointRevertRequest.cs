using System.Collections.Generic;

namespace Signet.App.ViewModels;

/// <summary>
/// A confirmation request for "Revert to before / after …" that would remove, bring back or rename files
/// (<see cref="MainWindowViewModel.RevertConfirmationRequested"/>).
/// </summary>
/// <param name="Forward"><c>true</c> for "Revert to after …", <c>false</c> for "Revert to before …".</param>
/// <param name="Title">The action label with the name of the target state.</param>
/// <param name="Rows">One row per affected file, sorted by path.</param>
public sealed record CheckpointRevertRequest(bool Forward, string Title, IReadOnlyList<CheckpointRevertRow> Rows);

/// <summary>A row of the revert confirmation.</summary>
/// <param name="Text">What happens to the file.</param>
/// <param name="LosesEdits">The file disappears together with edits made in it — shown as a warning.</param>
public sealed record CheckpointRevertRow(string Text, bool LosesEdits);
