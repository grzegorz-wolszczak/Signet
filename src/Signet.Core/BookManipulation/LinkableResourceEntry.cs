using Signet.Core.Resources;

namespace Signet.Core.BookManipulation;

/// <summary>
/// An entry of the "Link Stylesheets" / "Link Javascripts" map — the bookpath of a resource (CSS/JS) and whether
/// it is currently linked to all the selected (X)HTML files.
/// </summary>
/// <param name="BookPath">The bookpath of the stylesheet or script.</param>
/// <param name="Included">Whether the resource is linked to all the selected files.</param>
public readonly record struct LinkableResourceEntry(string BookPath, bool Included);

/// <summary>
/// The result of the "Link Stylesheets" / "Link Javascripts" operation: when a file is not well-formed,
/// the whole operation is cancelled.
/// </summary>
/// <param name="Applied">Whether the changes were applied.</param>
/// <param name="NotWellFormed">
/// The first file that failed the well-formed check (when <see cref="Applied"/> is
/// <c>false</c>), or <c>null</c>.
/// </param>
public sealed record LinkResourcesResult(bool Applied, HtmlResource? NotWellFormed);
