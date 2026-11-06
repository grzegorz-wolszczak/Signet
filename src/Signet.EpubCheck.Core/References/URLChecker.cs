using System;
using System.Text.RegularExpressions;
using Signet.EpubCheck.Core.Galimatias;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Opf;
using Signet.EpubCheck.Core.Reporting;
using Signet.EpubCheck.Core.Util;

namespace Signet.EpubCheck.Core.References;

/// <summary>
/// Resolves the URL strings of a document against its base and reports what is wrong with them (upstream's
/// <c>org.w3c.epubcheck.core.references.URLChecker</c>): a <c>file:</c> URL (<c>RSC-030</c>), a URL that only parses
/// leniently (<c>RSC-020</c>), a query in a URL of the container (<c>RSC-033</c>), a URL that leaves the container
/// (<c>RSC-026</c>). Whether a URL leaves the container is told by resolving it against two test bases on two hosts.
/// </summary>
public sealed partial class URLChecker
{
    private const string TestBaseAFull = "https://a.example.org/A/";
    private const string TestBaseAStart = "https://a.example.org/";
    private const string TestBaseBFull = "https://b.example.org/B/";
    private const string TestBaseBStart = "https://b.example.org/";
    private const string FileSchemePrefix = "file:";
    private const string DataSchemePrefix = "data:";

    private static readonly Url s_testBaseA = Url.Parse(TestBaseAFull);
    private static readonly Url s_testBaseB = Url.Parse(TestBaseBFull);

    private readonly ValidationContext _context;
    private Url _baseUrl;
    private Url _baseUrlTestA;
    private Url _baseUrlTestB;
    private bool _isRemoteBase;

    /// <summary>A checker of the URLs of the resource of <paramref name="context"/>, resolved against <paramref name="baseUrl"/>
    /// (its URL when <c>null</c>).</summary>
    public URLChecker(ValidationContext context, Url? baseUrl = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _baseUrl = baseUrl ?? context.Url;
        string relative = context.Relativize(_baseUrl);
        _baseUrlTestA = s_testBaseA.Resolve(relative);
        _baseUrlTestB = s_testBaseB.Resolve(relative);
    }

    /// <summary>Makes <paramref name="newBase"/> (resolved against the current base) the base; returns the base now in force.</summary>
    public Url SetBase(string newBase, EpubLocation location)
    {
        Url? url = Resolve(newBase, isBase: true, location);
        if (url is not null)
        {
            _baseUrl = url;
        }

        return _baseUrl;
    }

    /// <summary>The URL <paramref name="value"/> refers to, normalized; <c>null</c> when it does not parse (reported).</summary>
    public Url? CheckUrl(string? value, EpubLocation location) => EpubCheckUrlUtils.Normalize(Resolve(value, isBase: false, location));

    private Url? Resolve(string? value, bool isBase, EpubLocation location)
    {
        if (value is null)
        {
            return null;
        }

        IReport report = _context.Report;
        try
        {
            if (value.StartsWith(FileSchemePrefix, StringComparison.Ordinal))
            {
                report.Message(MessageId.RSC_030, location, value);
            }

            if (value.StartsWith(DataSchemePrefix, StringComparison.Ordinal))
            {
                value = WhiteSpace().Replace(value, string.Empty);
            }

            Url url = Url.Parse(_baseUrl, value);
            try
            {
                Url.Parse(_baseUrl, value, StrictErrorHandler.Instance);
            }
            catch (GalimatiasParseException e)
            {
                report.Message(MessageId.RSC_020, location, value, e.Message);
            }

            Url testA = _baseUrlTestA.Resolve(value);
            Url testB = _baseUrlTestB.Resolve(value);
            if (isBase)
            {
                _baseUrlTestA = testA;
                _baseUrlTestB = testB;
            }

            if (_isRemoteBase)
            {
                return url;
            }

            if (!testA.ToString().StartsWith(TestBaseAStart, StringComparison.Ordinal)
                || !testB.ToString().StartsWith(TestBaseBStart, StringComparison.Ordinal))
            {
                _isRemoteBase = true;
                return url;
            }

            if (url.Query is not null)
            {
                report.Message(MessageId.RSC_033, location, value);
                url = url.WithQuery(null);
            }

            if (_context.Container is not null && !isBase
                && (!testA.ToString().StartsWith(TestBaseAFull, StringComparison.Ordinal)
                    || !testB.ToString().StartsWith(TestBaseBFull, StringComparison.Ordinal)))
            {
                report.Message(MessageId.RSC_026, location, value);
            }

            return url;
        }
        catch (GalimatiasParseException e)
        {
            report.Message(MessageId.RSC_020, location, value, e.Message);
            return null;
        }
    }

    // Java's "\s": the ASCII white space.
    [GeneratedRegex("[ \\t\\n\\x0B\\f\\r]", RegexOptions.CultureInvariant)]
    private static partial Regex WhiteSpace();
}
