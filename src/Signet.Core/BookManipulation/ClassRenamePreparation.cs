using Signet.Core.Resources;

namespace Signet.Core.BookManipulation;

/// <summary>The result of <see cref="Book.PrepareClassRename"/> — the same well-formed guard as for other whole-book operations.</summary>
/// <param name="Renamer">The ready <see cref="ClassRenamer"/> or <c>null</c> when the operation was held back.</param>
/// <param name="NotWellFormed">The first not-well-formed file or <c>null</c>.</param>
public sealed record ClassRenamePreparation(ClassRenamer? Renamer, HtmlResource? NotWellFormed);
