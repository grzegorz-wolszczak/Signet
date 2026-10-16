using Signet.Core.Resources;

namespace Signet.Core.BookManipulation;

/// <summary>
/// The result of the whole-book operations that change the structure of the book ("Restructure Epub to Signet Norm",
/// "Use Standard File Extensions", "Rebase Manifest IDs") — the shared shape of the well-formed guard used by
/// <see cref="Book.RestructureToSignetNorm"/>, <see cref="Book.UseStandardFileExtensions"/>
/// and <see cref="Book.RebaseManifestIds"/> (which abort the operation at the first
/// file that is not well-formed, since they rewrite references across the whole book). Operations that work on
/// each file on its own return <see cref="FileBatchResult"/> instead.
/// </summary>
/// <param name="Applied">Whether the operation was carried out.</param>
/// <param name="NotWellFormed">
/// The first resource (HTML, OPF or NCX) that failed the well-formed check (when
/// <see cref="Applied"/> is <c>false</c>), or <c>null</c>.
/// </param>
public sealed record MaintenanceOperationResult(bool Applied, Resource? NotWellFormed)
{
    /// <summary>A shortcut for a successful operation (no not-well-formed file).</summary>
    public static MaintenanceOperationResult Ok { get; } = new(true, null);
}
