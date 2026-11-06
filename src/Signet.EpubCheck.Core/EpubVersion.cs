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

/// <summary>The text of an <see cref="EpubVersion"/> in messages (upstream's <c>EPUBVersion.toString</c>).</summary>
public static class EpubVersions
{
    /// <summary>The version text: <c>2.0.1</c>, <c>3.4</c> (upstream still says <c>3.3</c>) or <c>unknown</c>.</summary>
    public static string ToText(this EpubVersion version) => version switch
    {
        EpubVersion.Version2 => "2.0.1",
        EpubVersion.Version3 => "3.4",
        _ => "unknown",
    };
}
