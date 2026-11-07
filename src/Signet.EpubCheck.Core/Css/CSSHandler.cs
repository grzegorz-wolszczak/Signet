using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Signet.EpubCheck.Core.Galimatias;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Opf;
using Signet.EpubCheck.Core.References;
using Signet.EpubCheck.Core.Reporting;
using Signet.EpubCheck.Core.Vocab;

namespace Signet.EpubCheck.Core.Css;

/// <summary>Whether a style sheet is a file or a string in a content document.</summary>
internal enum CssMode
{
    /// <summary>A style sheet file.</summary>
    File,

    /// <summary>The text of a <c>style</c> element or attribute.</summary>
    String,
}

/// <summary>
/// Reads a style sheet for EPUBCheck (upstream's <c>CSSHandler</c>): the errors of the parser (<c>CSS-008</c>), the resources it
/// references (<c>@import</c>, <c>url()</c>, fonts: registered for the references checker), the fonts it declares, the outdated
/// <c>-epub-</c> properties, the properties of the item it needs (<c>remote-resources</c>) and a few rules of EPUB
/// (<c>CSS-001</c>, <c>CSS-002</c>, <c>CSS-006</c>, <c>CSS-007</c>, <c>CSS-019</c>, <c>CSS-028</c>, <c>CSS-029</c>).
/// </summary>
internal sealed partial class CSSHandler : ICssContentHandler, ICssErrorHandler
{
    private const string ImportRule = "@import";
    private const string NamespaceRule = "@namespace";
    private const string FontFaceRule = "@font-face";
    private const string EpubPrefix = "-epub-";
    private const string FullWidthValue = "-epub-fullwidth";
    private const string ActiveClass = ".-epub-media-overlay-active";
    private const string PlayingClass = ".-epub-media-overlay-playing";
    private const int StartingColumnNumber = 0;
    private const string SpaceAndQuotes = " \t\n\r\f\"'";

    private readonly ValidationContext _context;
    private readonly IReport _report;
    private readonly EpubVersion _version;
    private readonly CssMode _mode;
    private readonly URLChecker _urlChecker;
    private readonly Dictionary<string, Url?> _parsedUrls = new(StringComparer.Ordinal);
    private readonly SortedSet<PackageVocabs.ItemProperty> _detectedProperties = [];
    private int _startingLineNumber;
    private string? _fontFamily;
    private string? _fontStyle;
    private string? _fontWeight;
    private string? _fontUri;
    private bool _inFontFace;
    private bool _hasFontFaceDeclarations;
    private bool _inKeyFrames;
    private CssAtRule? _atRule;

    public CSSHandler(ValidationContext context, CssMode mode)
    {
        _context = context;
        _report = context.Report;
        _version = context.Version;
        _mode = mode;
        _urlChecker = new URLChecker(context);
    }

    /// <summary>Sets the line of the host document the CSS starts at (a value below 1 means the start of the document).</summary>
    public void SetStartingLineNumber(int offset)
    {
        _startingLineNumber = offset - 1;
        if (_startingLineNumber < 0)
        {
            _startingLineNumber = 0;
        }
    }

    /// <inheritdoc />
    public void HandleError(CssException exception)
    {
        // Upstream tests the text of the message of the parser: with an apostrophe, where the text has quotation marks.
        if (_inKeyFrames && InvalidTokenStringFinder().IsMatch(exception.Message))
        {
            return;
        }

        CssLocation location = exception.Location;
        _report.Message(MessageId.CSS_008, CorrectedLocation(location.Line, location.Column, null), exception.Message);
    }

    /// <inheritdoc />
    public void StartDocument()
    {
    }

    /// <inheritdoc />
    public void EndDocument() => CheckProperties();

    /// <inheritdoc />
    public void StartAtRule(CssAtRule atRule)
    {
        string ruleName = atRule.Name!;
        _atRule = atRule;
        if (ruleName == ImportRule)
        {
            CssConstruct uriOrString = atRule.Components[0];
            int line = uriOrString.Location.Line;
            int column = uriOrString.Location.Column;
            string? uri = null;
            if (uriOrString.Type == CssConstructType.Uri)
            {
                uri = ((CssURI)uriOrString).ToUriString();
            }
            else if (uriOrString.Type == CssConstructType.StringLiteral)
            {
                uri = uriOrString.ToCssString().Trim('"', '\'');
            }

            if (uri is not null)
            {
                ResolveAndRegister(uri, line, column, atRule.ToCssString(), ReferenceType.Stylesheet);
            }
        }
        else if (ruleName != NamespaceRule)
        {
            RegisterUris(atRule.Components, atRule.Location.Line, atRule.Location.Column);
        }

        if (ruleName == FontFaceRule)
        {
            _inFontFace = true;
        }
        else if (KeyframesPattern().IsMatch(ruleName))
        {
            _inKeyFrames = true;
        }
    }

    /// <inheritdoc />
    public void EndAtRule(string name)
    {
        if (_inFontFace)
        {
            _inFontFace = false;
            HandleFontFaceInfo();
            if (!_hasFontFaceDeclarations)
            {
                _report.Message(MessageId.CSS_019, _context.Location().At(_atRule!.Location.Line, _atRule.Location.Column).WithContext(_atRule.ToCssString()));
            }

            _hasFontFaceDeclarations = false;
        }

        if (_inKeyFrames)
        {
            _inKeyFrames = false;
        }

        _atRule = null;
    }

    /// <inheritdoc />
    public void Selectors(IReadOnlyList<CssSelector> selectors)
    {
        foreach (CssSelector selector in selectors)
        {
            if (!_context.FeatureReport.HasFeature(FeatureKind.MediaOverlaysActiveClass) && FindClassName(selector, ActiveClass))
            {
                _report.Message(
                    MessageId.CSS_029,
                    CorrectedLocation(selector.Location.Line, selector.Location.Column, selector.ToCssString()),
                    "-epub-media-overlay-active",
                    "media:active-class");
            }

            if (!_context.FeatureReport.HasFeature(FeatureKind.MediaOverlaysPlaybackActiveClass) && FindClassName(selector, PlayingClass))
            {
                _report.Message(
                    MessageId.CSS_029,
                    CorrectedLocation(selector.Location.Line, selector.Location.Column, selector.ToCssString()),
                    "-epub-media-overlay-playing",
                    "media:playback-active-class");
            }
        }
    }

    /// <inheritdoc />
    public void EndSelectors(IReadOnlyList<CssSelector> selectors)
    {
    }

    /// <inheritdoc />
    public void Declaration(CssDeclaration declaration)
    {
        RegisterUris(declaration.Components, declaration.Location.Line, declaration.Location.Column);
        string? propertyName = declaration.Name;
        if (propertyName is null)
        {
            return;
        }

        if (propertyName.StartsWith(EpubPrefix, StringComparison.Ordinal))
        {
            _report.Message(MessageId.OBS_001, CorrectedLocation(declaration), "css-prefixed-property", propertyName);
        }
        else if (propertyName == "text-transform" && declaration.Components.Any(component => component.ToCssString() == FullWidthValue))
        {
            _report.Message(MessageId.OBS_001, CorrectedLocation(declaration), "css-prefixed-value", FullWidthValue);
        }

        if (propertyName == "position")
        {
            string value = declaration.Components[0].ToCssString();
            if (string.Equals(value, "fixed", StringComparison.OrdinalIgnoreCase))
            {
                _report.Message(MessageId.CSS_006, CorrectedLocation(declaration));
            }
        }

        if (_version == EpubVersion.Version3 && propertyName is "direction" or "unicode-bidi")
        {
            _report.Message(MessageId.CSS_001, CorrectedLocation(declaration), propertyName);
        }

        if (!_inFontFace)
        {
            return;
        }

        _hasFontFaceDeclarations = true;
        switch (propertyName)
        {
            case "font-family":
                _fontFamily = declaration.Components[0].ToCssString().Trim(SpaceAndQuotes.ToCharArray());
                break;
            case "font-style":
                _fontStyle = declaration.Components[0].ToCssString();
                break;
            case "font-weight":
                _fontWeight = declaration.Components[0].ToCssString();
                break;
            case "src":
                foreach (CssConstruct construct in declaration.Components.Where(component => component.Type == CssConstructType.Uri))
                {
                    if (_parsedUrls.GetValueOrDefault(((CssURI)construct).ToUriString()) is { } fontUrl && _context.ResourceRegistry is not null)
                    {
                        _fontUri = _context.Relativize(fontUrl);
                        if (_context.GetMimeType(fontUrl) is { } fontMimeType)
                        {
                            bool blessed = _version switch
                            {
                                EpubVersion.Version2 => OpfMediaTypes.IsBlessedFontMimeType20(fontMimeType),
                                EpubVersion.Version3 => OpfMediaTypes.IsBlessedFontType(fontMimeType),
                                _ => true,
                            };
                            if (!blessed)
                            {
                                _report.Message(MessageId.CSS_007, CorrectedLocation(declaration), fontUrl, fontMimeType);
                            }
                        }
                    }
                }

                break;
        }

        _report.Message(
            MessageId.CSS_028,
            CorrectedLocation(declaration.Location.Line, declaration.Location.Column, _fontUri ?? "null"));
    }

    private EpubLocation CorrectedLocation(int lineNumber, int columnNumber, string? details)
    {
        lineNumber = CorrectedLineNumber(lineNumber);
        columnNumber = CorrectedColumnNumber(lineNumber, columnNumber);
        return _context.Location().At(lineNumber, columnNumber).WithContext(details);
    }

    private EpubLocation CorrectedLocation(CssConstruct construct)
    {
        int line = CorrectedLineNumber(construct.Location.Line);
        int column = CorrectedColumnNumber(line, construct.Location.Column);
        return _context.Location().At(line, column).WithContext(construct.ToCssString());
    }

    private int CorrectedLineNumber(int lineNumber) => _startingLineNumber + lineNumber;

    private static int CorrectedColumnNumber(int lineNumber, int columnNumber) => lineNumber != 0 ? columnNumber : StartingColumnNumber + columnNumber;

    private static bool FindClassName(CssConstruct construct, string name)
    {
        if (construct.Type == CssConstructType.ClassName && name == construct.ToCssString())
        {
            return true;
        }

        return construct is CssComposedConstruct composed && composed.Components.Any(component => FindClassName(component, name));
    }

    private void RegisterUris(List<CssConstruct> constructs, int line, int column)
    {
        foreach (CssConstruct construct in constructs.Where(construct => construct.Type == CssConstructType.Uri))
        {
            ResolveAndRegister(((CssURI)construct).ToUriString(), line, column, construct.ToCssString(), _inFontFace ? ReferenceType.Font : ReferenceType.Generic);
        }
    }

    private void ResolveAndRegister(string uriString, int line, int column, string cssContext, ReferenceType type)
    {
        if (uriString.Trim().Length > 0)
        {
            // a fragment-only reference points into the style sheet itself
            if (!uriString.StartsWith('#'))
            {
                Url? url = _urlChecker.CheckUrl(uriString, CorrectedLocation(line, column, cssContext));
                _parsedUrls[uriString] = url;
                if (url is not null && _context.ReferenceRegistry is not null)
                {
                    _context.ReferenceRegistry.RegisterReference(url, type, CorrectedLocation(line, column, cssContext));
                    if (type != ReferenceType.Stylesheet && _context.IsRemote(url))
                    {
                        _detectedProperties.Add(PackageVocabs.ItemProperty.RemoteResources);
                    }
                }
            }
        }
        else
        {
            _report.Message(MessageId.CSS_002, CorrectedLocation(line, column, cssContext));
        }
    }

    private void HandleFontFaceInfo()
    {
        if (_fontFamily is null)
        {
            return;
        }

        string description = _fontFamily
            + (_fontStyle is not null && !string.Equals("normal", _fontStyle, StringComparison.OrdinalIgnoreCase) ? "," + _fontStyle : string.Empty)
            + (_fontWeight is not null && !string.Equals("normal", _fontWeight, StringComparison.OrdinalIgnoreCase) ? "," + _fontWeight : string.Empty);
        if (_fontUri is not null && !_fontUri.StartsWith("http", StringComparison.Ordinal))
        {
            _report.Info(_context.Path, FeatureKind.FontEmbedded, description);
        }
        else
        {
            _report.Info(_context.Path, FeatureKind.FontReference, description);
            if (_fontUri is not null)
            {
                _report.Info(_context.Path, FeatureKind.Reference, _fontUri);
            }
        }
    }

    private void CheckProperties()
    {
        // single file validation
        if (_context.Container is null)
        {
            return;
        }

        SortedSet<PackageVocabs.ItemProperty> declared = [.. VocabProperty.Filter<PackageVocabs.ItemProperty>(_context.Properties)];
        foreach (PackageVocabs.ItemProperty property in _detectedProperties.Where(property => !declared.Contains(property)))
        {
            _report.Message(MessageId.OPF_014, _context.Location().At(_startingLineNumber, StartingColumnNumber), PackageVocabs.ItemVocab.GetName(property));
        }

        if (_mode == CssMode.File)
        {
            // The property cannot be checked as unused in a string: the rest of the document may need it.
            if (declared.Contains(PackageVocabs.ItemProperty.RemoteResources) && !_detectedProperties.Contains(PackageVocabs.ItemProperty.RemoteResources))
            {
                _report.Message(MessageId.OPF_018, _context.Location().At(_startingLineNumber, StartingColumnNumber));
            }
        }
    }

    [GeneratedRegex("^Token '[0-9]+%' not allowed here$", RegexOptions.CultureInvariant)]
    private static partial Regex InvalidTokenStringFinder();

    [GeneratedRegex("^@((keyframes)|(-moz-keyframes)|(-webkit-keyframes)|(-o-keyframes))$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyframesPattern();
}
