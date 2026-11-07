using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Signet.EpubCheck.Core.Css;
using Signet.EpubCheck.Core.Galimatias;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Opf;
using Signet.EpubCheck.Core.References;
using Signet.EpubCheck.Core.Reporting;
using Signet.EpubCheck.Core.Util;
using Signet.EpubCheck.Core.Util.Mime;
using Signet.EpubCheck.Core.Xml;

namespace Signet.EpubCheck.Core.Ops;

/// <summary>
/// Reads an XHTML or SVG content document of EPUB 2, and the base of the handler of EPUB 3 (upstream's <c>OPSHandler</c>): the
/// references of the document (links, images, objects, style sheets, scripts, SVG paints and symbols), its ids, its styles
/// (handed to the CSS checker), scripts, the accessibility of its tables (<c>ACC-005</c>, <c>ACC-006</c>, <c>ACC-012</c>) and
/// the use of <c>epub:type</c> (<c>ACC-007</c>).
/// </summary>
public class OPSHandler : XmlHandler
{
    private const string StyleName = "style";
    private const string SvgPaintFill = "fill";
    private const string SvgPaintStroke = "stroke";
    private const string SvgClipPath = "clip-path";
    private const string UrlFunctionStart = "url(";
    private const string UrlFunctionEnd = ")";
    private const string XmlStylesheetTarget = "xml-stylesheet";
    private const string StylesheetRel = "stylesheet";
    private const string SelfReference = ".";

    private readonly Stack<EpubLocation> _elementLocations = new();
    private long _openElements;
    private long _charsCount;
    private int _tableDepth;
    private bool _hasTh;
    private bool _hasThead;
    private bool _hasCaption;
    private bool _epubTypeInUse;
    private StringBuilder? _textNode;

    /// <summary>A handler of the content document of <paramref name="context"/>.</summary>
    public OPSHandler(ValidationContext context)
        : base(context)
    {
    }

    /// <summary>Whether the document has CSS: a style sheet it links, an <c>xml-stylesheet</c> or a <c>style</c> element.</summary>
    protected bool HasCss { get; set; }

    /// <summary>The text of the <c>style</c> element being read, or <c>null</c>.</summary>
    protected StringBuilder? TextNode => _textNode;

    /// <summary>The number of elements open.</summary>
    protected long OpenElements => _openElements;

    /// <summary>The attribute <paramref name="attributeName"/> of the SVG element when it is a <c>url(…)</c> value.</summary>
    private void CheckSvgAttributeUrlValue(string attributeName)
    {
        string? value = CurrentElement.GetAttribute(attributeName);

        // Upstream's TODO: this should be a proper parse of the URL value (https://drafts.csswg.org/css-values-3/#urls).
        if (value is null || !value.StartsWith(UrlFunctionStart, StringComparison.Ordinal) || !value.EndsWith(UrlFunctionEnd, StringComparison.Ordinal))
        {
            return;
        }

        // Upstream reads value[0] of what is between the parentheses and fails on "url()" and on "url(')".
        value = value[UrlFunctionStart.Length..^UrlFunctionEnd.Length];
        if (value.Length >= 2 && ((value[0] == '\'' && value[^1] == '\'') || (value[0] == '"' && value[^1] == '"')))
        {
            value = value[1..^1];
        }

        Url? url = CheckUrl(value);
        switch (attributeName)
        {
            case SvgPaintFill:
            case SvgPaintStroke:
                RegisterReference(url, ReferenceType.SvgPaint);
                break;
            case SvgClipPath:
                RegisterReference(url, ReferenceType.SvgClipPath);
                break;
        }
    }

    /// <summary>Checks the URL of an image and registers it.</summary>
    protected virtual void CheckImage(string? href)
    {
        Url? imageUrl = CheckUrl(href);
        if (imageUrl is not null)
        {
            RegisterReference(imageUrl, ReferenceType.Image);
        }
    }

    /// <summary>Checks the <c>data</c> of an <c>object</c> and registers it.</summary>
    protected virtual void CheckObject()
    {
        Url? objectUrl = CheckUrl(CurrentElement.GetAttribute("data"));
        if (objectUrl is not null)
        {
            RegisterReference(objectUrl, ReferenceType.Generic);
        }
    }

    /// <summary>Checks a <c>link</c>: a style sheet it links is registered.</summary>
    protected virtual void CheckLink()
    {
        XMLElement element = CurrentElement;
        Url? href = CheckUrl(element.GetAttribute("href"));
        string? rel = element.GetAttribute("rel");
        if (href is not null && rel is not null && rel.ToLowerInvariant().Contains(StylesheetRel, StringComparison.Ordinal))
        {
            HasCss = true;
            RegisterReference(href, ReferenceType.Stylesheet);
        }
    }

    /// <summary>Checks the reference of a <c>use</c> of an SVG symbol.</summary>
    protected void CheckSymbol()
    {
        foreach (string? href in GetSvgHrefs())
        {
            if (CheckUrl(href) is { } url)
            {
                RegisterReference(url, ReferenceType.SvgSymbol);
            }
        }
    }

    /// <summary>The references the SVG element has (its <c>xlink:href</c>; EPUB 3 also has <c>href</c>).</summary>
    protected virtual IReadOnlyList<string?> GetSvgHrefs() => [CurrentElement.GetAttributeNS(Namespaces.XLink, "href")];

    private void CheckHRef(string? href)
    {
        if (href is null)
        {
            return;
        }

        href = JavaStrings.Trim(href);
        if (href.Length == 0)
        {
            // href="" is a reference to the document itself, which is valid, but issue 225 asks for a hint.
            Report.Message(MessageId.HTM_045, Location());
            return;
        }

        if (href == SelfReference)
        {
            return;
        }

        Url? url = CheckUrl(href);

        // The URL did not parse (reported).
        if (url is null)
        {
            return;
        }

        if (Context.IsRemote(url))
        {
            Report.Info(Path, FeatureKind.Reference, href);
            if (!UriSchemes.Contains(url.Scheme))
            {
                Report.Message(MessageId.HTM_025, Location().WithContext(href));
            }

            return;
        }

        ProcessHyperlink(url);
    }

    /// <summary>Checks the <c>font-face-uri</c> of an SVG font and registers the fonts.</summary>
    protected virtual IReadOnlyList<Url> CheckSvgFontFaceUri()
    {
        List<Url> urls = [.. GetSvgHrefs().Select(CheckUrl).OfType<Url>()];
        foreach (Url url in urls)
        {
            RegisterReference(url, ReferenceType.Font);
        }

        return urls;
    }

    /// <summary>Registers a hyperlink to a resource of the publication.</summary>
    protected virtual void ProcessHyperlink(Url href) => RegisterReference(href, ReferenceType.Hyperlink);

    /// <inheritdoc />
    protected override void StartElement()
    {
        _openElements++;
        XMLElement e = CurrentElement;
        _elementLocations.Push(Location());

        string? id = e.GetAttribute("id");

        if (!_epubTypeInUse && e.GetAttributeNS(Namespaces.Ops, "type") is not null)
        {
            _epubTypeInUse = true;
        }

        string? ns = e.Namespace;
        string name = e.Name.ToLowerInvariant();
        ReferenceType resourceType = ReferenceType.Generic;
        if (ns is not null)
        {
            if (name == StyleName)
            {
                _textNode = new StringBuilder();
            }

            if (ns == Namespaces.Svg)
            {
                switch (name)
                {
                    case "lineargradient":
                    case "radialgradient":
                    case "pattern":
                        resourceType = ReferenceType.SvgPaint;
                        break;
                    case "clippath":
                        resourceType = ReferenceType.SvgClipPath;
                        break;
                    case "symbol":
                        resourceType = ReferenceType.SvgSymbol;
                        break;
                    case "a":
                        foreach (string? href in GetSvgHrefs())
                        {
                            CheckHRef(href);
                        }

                        break;
                    case "use":
                        CheckSymbol();
                        break;
                    case "image":
                        foreach (string? href in GetSvgHrefs())
                        {
                            CheckImage(href);
                        }

                        break;
                    case "font-face-uri":
                        CheckSvgFontFaceUri();
                        break;
                    case "script":
                        CheckScript();
                        break;
                }

                CheckSvgAttributeUrlValue(SvgPaintFill);
                CheckSvgAttributeUrlValue(SvgPaintStroke);
                CheckSvgAttributeUrlValue(SvgClipPath);
            }
            else if (ns == Namespaces.Xhtml)
            {
                switch (name)
                {
                    case "a":
                    case "area":
                        CheckHRef(e.GetAttribute("href"));
                        break;
                    case "img":
                        CheckImage(e.GetAttribute("src"));
                        break;
                    case "object":
                        CheckObject();
                        break;
                    case "link":
                        CheckLink();
                        break;
                    case "iframe":
                        CheckIFrame();
                        break;
                    case "table":
                        ++_tableDepth;
                        break;
                    case "th" when _tableDepth > 0:
                        _hasTh = true;
                        break;
                    case "thead" when _tableDepth > 0:
                        _hasThead = true;
                        break;
                    case "caption" when _tableDepth > 0:
                        _hasCaption = true;
                        break;
                    case "i":
                    case "b":
                    case "em":
                    case "strong":
                        CheckBoldItalics();
                        break;
                    case "script":
                        CheckScript();
                        break;
                }

                string? style = e.GetAttribute("style");
                if (!string.IsNullOrEmpty(style))
                {
                    new CSSChecker(Context, style, Location().Line, isStyleAttribute: true).Check();
                }
            }
        }

        if (Context.ResourceRegistry is not null && id is not null)
        {
            Context.ResourceRegistry.RegisterId(id, resourceType, Context.Url);
        }
    }

    /// <summary>Checks a bold or italic element (nothing in EPUB 2 and 3).</summary>
    protected virtual void CheckBoldItalics()
    {
    }

    /// <summary>Checks an <c>iframe</c>.</summary>
    protected virtual void CheckIFrame()
    {
    }

    /// <summary>Checks a <c>script</c>: JavaScript is reported as a feature.</summary>
    protected virtual void CheckScript()
    {
        string? type = CurrentElement.GetAttribute("type");
        if (type is null || OpfMediaTypes.IsScriptType(type))
        {
            ProcessJavascript();
        }
    }

    /// <summary>The document has JavaScript.</summary>
    protected virtual void ProcessJavascript()
    {
        Report.Info(Path, FeatureKind.HasScripts, string.Empty);
        Context.FeatureReport.Report(FeatureKind.HasScripts, Location());
    }

    /// <inheritdoc />
    protected override void EndElement()
    {
        _openElements--;
        XMLElement e = CurrentElement;
        string? ns = e.Namespace;
        string name = e.Name;

        if (_openElements == 0)
        {
            Report.Info(Path, FeatureKind.CharsCount, _charsCount.ToString(CultureInfo.InvariantCulture));
            if (!_epubTypeInUse)
            {
                if (Context.Version == EpubVersion.Version3)
                {
                    Report.Message(MessageId.ACC_007, Context.Location());
                }
            }
            else
            {
                _epubTypeInUse = false;
            }
        }

        EpubLocation currentLocation = _elementLocations.Pop();

        if (name == StyleName)
        {
            string style = _textNode?.ToString() ?? string.Empty;
            if (style.Length > 0)
            {
                HasCss = true;
                new CSSChecker(Context, style, currentLocation.Line, isStyleAttribute: false).Check();
            }

            _textNode = null;
        }

        if (ns == Namespaces.Xhtml && name == "table" && _tableDepth > 0)
        {
            --_tableDepth;
            EpubLocation location = currentLocation.WithContext("table");
            CheckDependentCondition(MessageId.ACC_005, _tableDepth == 0, _hasTh, location);
            CheckDependentCondition(MessageId.ACC_006, _tableDepth == 0, _hasThead, location);
            CheckDependentCondition(MessageId.ACC_012, _tableDepth == 0, _hasCaption, location);
            _hasTh = _hasThead = _hasCaption = false;
        }
    }

    /// <summary>Reports <paramref name="id"/> when the primary condition <paramref name="condition1"/> is true but the dependent <paramref name="condition2"/> is false.</summary>
    protected void CheckDependentCondition(MessageId id, bool condition1, bool condition2, EpubLocation location)
    {
        if (condition1 && !condition2)
        {
            Report.Message(id, location);
        }
    }

    /// <inheritdoc />
    public override void Characters(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _charsCount += text.Length;
        _textNode?.Append(text);
    }

    /// <inheritdoc />
    public override void ProcessingInstruction(string target, string data)
    {
        base.ProcessingInstruction(target, data);

        // For SVG documents, the 'xml-stylesheet' processing instructions are read.
        if (MediaTypes.Is(MediaTypes.Svg, Context.MimeType) && target == XmlStylesheetTarget)
        {
            CheckXmlStylesheetPI(data);
        }
    }

    /// <summary>Reads the <c>href</c> of an <c>xml-stylesheet</c> processing instruction of a CSS style sheet.</summary>
    protected void CheckXmlStylesheetPI(string data)
    {
        string? type = ProcInstParser.GetPseudoAttribute(data, "type");
        if (type is null || MediaTypes.Is(MediaTypes.Css, type))
        {
            string? href = ProcInstParser.GetPseudoAttribute(data, "href");
            if (CheckUrl(href) is { } url)
            {
                HasCss = true;
                RegisterReference(url, ReferenceType.Stylesheet);
            }
        }
    }
}
