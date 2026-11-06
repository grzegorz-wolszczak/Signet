namespace Signet.EpubCheck.Core.Opf;

/// <summary>A publication type declared by <c>dc:type</c> that changes what is checked (upstream's <c>PublicationType</c>).</summary>
public enum PublicationType
{
    /// <summary><c>epub</c>.</summary>
    Epub,

    /// <summary><c>dictionary</c>.</summary>
    Dictionary,

    /// <summary><c>edupub</c>.</summary>
    Edupub,

    /// <summary><c>index</c>.</summary>
    Index,

    /// <summary><c>preview</c>.</summary>
    Preview,
}
