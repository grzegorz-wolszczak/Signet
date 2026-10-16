namespace Signet.Core.BookManipulation;

/// <summary>
/// An entry of the "Link Stylesheets" / "Link Javascripts" map — the bookpath of a resource (CSS/JS) and whether
/// it is currently linked to all the selected (X)HTML files.
/// </summary>
/// <param name="BookPath">The bookpath of the stylesheet or script.</param>
/// <param name="Included">Whether the resource is linked to all the selected files.</param>
public readonly record struct LinkableResourceEntry(string BookPath, bool Included);
