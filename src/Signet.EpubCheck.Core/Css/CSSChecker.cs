using System;
using System.Globalization;
using System.IO;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Opf;

namespace Signet.EpubCheck.Core.Css;

/// <summary>
/// The checker of CSS (upstream's <c>CSSChecker</c>), for a style sheet or for the text of a <c>style</c> element or attribute: it
/// parses the style sheet with <see cref="CssParser"/> and lets <see cref="CSSHandler"/> report what is wrong with it and register
/// what it references. A style sheet that is not UTF-8 is reported (<c>CSS-003</c>, <c>CSS-004</c>).
/// </summary>
public sealed class CSSChecker : PublicationResourceChecker
{
    private readonly CssMode _mode;
    private readonly string? _value;
    private readonly bool _isStyleAttribute;
    private int _line;

    /// <summary>A checker of the style sheet of <paramref name="context"/>.</summary>
    public CSSChecker(ValidationContext context)
        : base(context)
    {
        _mode = CssMode.File;
        _line = -1;
    }

    /// <summary>A checker of the CSS <paramref name="value"/>, which starts at line <paramref name="line"/> of the document of
    /// <paramref name="context"/>: the value of a <c>style</c> attribute when <paramref name="isStyleAttribute"/> is true, else the
    /// content of a <c>style</c> element.</summary>
    public CSSChecker(ValidationContext context, string value, int line, bool isStyleAttribute)
        : base(context)
    {
        _mode = CssMode.String;
        _value = value ?? throw new ArgumentNullException(nameof(value));
        _line = line;
        _isStyleAttribute = isStyleAttribute;
    }

    /// <inheritdoc />
    protected override bool CheckPublicationBeforeContent() => _mode == CssMode.String || base.CheckPublicationBeforeContent();

    /// <inheritdoc />
    protected override bool CheckContent()
    {
        try
        {
            CSSHandler handler = new(Context, _mode);
            if (_mode == CssMode.String && _line > -1)
            {
                handler.SetStartingLineNumber(_line);
            }

            CssSource? source = OpenSource();
            Parse(source, handler);
            handler.SetStartingLineNumber(-1);
            _line = -1;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Report.Message(MessageId.PKG_008, Context.Location(), e.Message);
        }

        return true;
    }

    private CssSource? OpenSource()
    {
        if (_mode != CssMode.File)
        {
            return null;
        }

        CssSource source;
        using (Stream stream = Context.Resources.OpenStream(Context.Url))
        {
            source = new CssSource(Context.Path, stream);
        }

        string? charset = source.Bom ?? source.Charset;
        if (charset is not null)
        {
            charset = charset.ToLowerInvariant();
            if (charset != "utf-8")
            {
                Report.Message(charset.StartsWith("utf-16", StringComparison.Ordinal) ? MessageId.CSS_003 : MessageId.CSS_004, Context.Location(), charset);
            }
        }

        return source;
    }

    private void Parse(CssSource? source, CSSHandler handler)
    {
        CultureInfo? culture = Context.Culture;
        CssParser parser = new(culture);
        if (_isStyleAttribute)
        {
            using StringReader reader = new(_value!);
            parser.ParseStyleAttribute(reader, Context.Path, handler, handler);
        }
        else if (_mode == CssMode.File)
        {
            parser.Parse(source!, handler, handler);
        }
        else
        {
            using StringReader reader = new(_value!);
            parser.Parse(reader, Context.Path, handler, handler);
        }
    }
}
