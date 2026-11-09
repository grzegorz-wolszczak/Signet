using System;
using Signet.Core.Resources;

namespace Signet.Core.BookManipulation;

/// <summary>
/// Adds, in one (X)HTML file, the ARIA roles that the <c>epub:type</c> of its elements ask for and that the elements lack
/// (<see cref="AriaRoleAdder"/>): the fix of <c>Validation_Accessibility_MissingRole</c>. It works on the whole file, like the
/// fix of Calibre's check, so the first of its results that is fixed fixes the others of the file.
/// </summary>
public sealed class AddAriaRolesFix : ValidationFix
{
    private readonly string _bookPath;

    /// <summary>Creates the fix for the file <paramref name="bookPath"/>.</summary>
    /// <param name="bookPath">The file.</param>
    public AddAriaRolesFix(string bookPath)
    {
        _bookPath = bookPath;
    }

    /// <inheritdoc/>
    public override bool Apply(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);
        if (book.GetFolderKeeper().GetResourceByBookPathNoThrow(_bookPath) is not HtmlResource html)
        {
            return false;
        }

        string text = html.GetText();
        return ReferenceFix.SetIfChanged(html, text, AriaRoleAdder.Add(text));
    }
}
