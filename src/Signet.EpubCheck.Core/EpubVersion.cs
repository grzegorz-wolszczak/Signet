namespace Signet.EpubCheck.Core;

/// <summary>The version of EPUB a publication or a document is checked against (upstream's <c>EPUBVersion</c>).</summary>
public enum EpubVersion
{
    /// <summary>Not known (yet).</summary>
    Unknown = 0,

    /// <summary>EPUB 2 (OPF 2.0.1, NCX, XHTML 1.1).</summary>
    Version2,

    /// <summary>EPUB 3 (checked against EPUB 3.4).</summary>
    Version3,
}
