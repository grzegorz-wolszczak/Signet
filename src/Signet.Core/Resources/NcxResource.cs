using System;
using Signet.Core.Toc;

namespace Signet.Core.Resources;

/// <summary>
/// An NCX resource (the EPUB 2 table of contents / the compatibility one in EPUB 3).
/// </summary>
public sealed class NcxResource : XmlResource
{
    private const string IdPlaceholder = "ID_UNKNOWN";

    private const string Template2 =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
        "<!DOCTYPE ncx PUBLIC \"-//NISO//DTD ncx 2005-1//EN\"\n" +
        "   \"http://www.daisy.org/z3986/2005/ncx-2005-1.dtd\">\n" +
        "<ncx xmlns=\"http://www.daisy.org/z3986/2005/ncx/\" version=\"2005-1\">\n" +
        "  <head>\n" +
        "    <meta name=\"dtb:uid\" content=\"ID_UNKNOWN\" />\n" +
        "    <meta name=\"dtb:depth\" content=\"0\" />\n" +
        "    <meta name=\"dtb:totalPageCount\" content=\"0\" />\n" +
        "    <meta name=\"dtb:maxPageNumber\" content=\"0\" />\n" +
        "  </head>\n" +
        "<docTitle>\n" +
        "  <text>Unknown</text>\n" +
        "</docTitle>\n" +
        "<navMap>\n" +
        "<navPoint id=\"navPoint-1\" playOrder=\"1\">\n" +
        "  <navLabel>\n" +
        "    <text>{0}</text>\n" +
        "  </navLabel>\n" +
        "  <content src=\"{1}\" />\n" +
        "</navPoint>\n" +
        "</navMap>\n" +
        "</ncx>";

    // under xhtml5 / epub3 a doctype is not allowed for ncx
    private const string Template3 =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
        "<ncx xmlns=\"http://www.daisy.org/z3986/2005/ncx/\" version=\"2005-1\">\n" +
        "  <head>\n" +
        "    <meta name=\"dtb:uid\" content=\"ID_UNKNOWN\" />\n" +
        "    <meta name=\"dtb:depth\" content=\"0\" />\n" +
        "    <meta name=\"dtb:totalPageCount\" content=\"0\" />\n" +
        "    <meta name=\"dtb:maxPageNumber\" content=\"0\" />\n" +
        "  </head>\n" +
        "<docTitle>\n" +
        "   <text>Unknown</text>\n" +
        "</docTitle>\n" +
        "<navMap>\n" +
        "<navPoint id=\"navPoint-1\" playOrder=\"1\">\n" +
        "  <navLabel>\n" +
        "    <text>{0}</text>\n" +
        "  </navLabel>\n" +
        "  <content src=\"{1}\" />\n" +
        "</navPoint>\n" +
        "</navMap>\n" +
        "</ncx>";

    /// <inheritdoc cref="Resource(string, string)"/>
    public NcxResource(string mainFolder, string fullFilePath)
        : base(mainFolder, fullFilePath)
    {
    }

    /// <inheritdoc/>
    public override ResourceType Type => ResourceType.Ncx;

    /// <summary>Parses the current resource text into an <see cref="NcxDocument"/> model (leniently).</summary>
    public NcxDocument GetNcxDocument() => NcxDocument.Parse(GetText());

    /// <summary>Serializes <paramref name="document"/> and stores it as the new resource text.</summary>
    public void SetNcxDocument(NcxDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        SetText(document.ToXml());
    }

    /// <summary>Substitutes the publication identifier for <c>ID_UNKNOWN</c>.</summary>
    public void SetMainId(string mainId)
    {
        ArgumentNullException.ThrowIfNull(mainId);
        SetText(GetText().Replace(IdPlaceholder, mainId, StringComparison.Ordinal));
    }

    /// <summary>
    /// Fills the resource with a default NCX pointing at the first text section under
    /// <paramref name="startBookPath"/>.
    /// </summary>
    /// <param name="epubVersion">The EPUB version (<c>""</c> =&gt; same as <see cref="Resource.EpubVersion"/>).</param>
    /// <param name="startBookPath">The bookpath of the first section the <c>navPoint</c> should point at.</param>
    public void FillWithDefaultText(string epubVersion, string startBookPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(startBookPath);
        string version = string.IsNullOrEmpty(epubVersion) ? EpubVersion : epubVersion;
        string textHref = Utility.UrlEncodePath(Core.BookPath.Relative(BookPath, startBookPath));
        string template = version.StartsWith('2') ? Template2 : Template3;
        SetText(string.Format(System.Globalization.CultureInfo.InvariantCulture, template, "Start", textHref));
    }
}
