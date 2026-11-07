using System;
using Signet.EpubCheck.Core.Opf;

namespace Signet.EpubCheck.Core.Css;

/// <summary>
/// The checker of CSS (upstream's <c>CSSChecker</c>), for a style sheet or for the text of a <c>style</c> element or attribute.
/// Its parser and its checks are task 3.7; until then it reads nothing and reports nothing, but the content documents already
/// hand it their styles, so that task changes this class alone.
/// </summary>
public sealed class CSSChecker : PublicationResourceChecker
{
    private readonly string? _value;
    private readonly int _line;
    private readonly bool _isStyleAttribute;

    /// <summary>A checker of the style sheet of <paramref name="context"/>.</summary>
    public CSSChecker(ValidationContext context)
        : base(context)
    {
        _line = -1;
    }

    /// <summary>A checker of the CSS <paramref name="value"/>, which starts at line <paramref name="line"/> of the document of
    /// <paramref name="context"/>: the value of a <c>style</c> attribute when <paramref name="isStyleAttribute"/> is true, else the
    /// content of a <c>style</c> element.</summary>
    public CSSChecker(ValidationContext context, string value, int line, bool isStyleAttribute)
        : base(context)
    {
        _value = value ?? throw new ArgumentNullException(nameof(value));
        _line = line;
        _isStyleAttribute = isStyleAttribute;
    }

    /// <summary>The CSS being checked, or <c>null</c> for a style sheet file.</summary>
    internal string? Value => _value;

    /// <summary>The line the CSS starts at.</summary>
    internal int Line => _line;

    /// <summary>Whether the CSS is the value of a <c>style</c> attribute.</summary>
    internal bool IsStyleAttribute => _isStyleAttribute;

    /// <inheritdoc />
    protected override bool CheckPublicationBeforeContent() => _value is not null || base.CheckPublicationBeforeContent();
}
