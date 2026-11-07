using System.Collections.Generic;
using Signet.EpubCheck.Core.Galimatias;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Opf;
using Signet.EpubCheck.Core.Ops;
using Signet.EpubCheck.Core.References;
using Signet.EpubCheck.Core.Reporting;
using Signet.EpubCheck.Core.Vocab;
using Signet.EpubCheck.Core.Xml;

namespace Signet.EpubCheck.Core.Nav;

/// <summary>
/// Reads the navigation document (upstream's <c>NavHandler</c>, an <see cref="OPSHandler30"/>): the kind of each <c>nav</c>
/// (<c>toc</c>, <c>page-list</c>, <c>landmarks</c>, lists of figures, tables, audio and video), and the links of the
/// <c>toc</c> and <c>page-list</c>, which are registered to be checked against the reading order.
/// </summary>
public class NavHandler : OPSHandler30
{
    private NavType _currentNavType = NavType.None;
    private bool _isNavTypes;

    /// <summary>A handler of the navigation document of <paramref name="context"/>.</summary>
    public NavHandler(ValidationContext context)
        : base(context)
    {
    }

    private enum NavType
    {
        None,
        Toc,
        Landmarks,
        PageList,
        Other,
    }

    // The text of a kind of nav in messages (upstream's NavType.toString: the name in lower case with hyphens).
    private static string Text(NavType type) => type switch
    {
        NavType.None => "none",
        NavType.Toc => "toc",
        NavType.Landmarks => "landmarks",
        NavType.PageList => "page-list",
        _ => "other",
    };

    /// <inheritdoc />
    protected override void StartElement()
    {
        base.StartElement();
        XMLElement e = CurrentElement;
        if (e.Namespace != Namespaces.Xhtml || e.Name != "a")
        {
            return;
        }

        string? href = e.GetAttribute("href");
        Url? url = CheckUrl(href);
        if (url is null)
        {
            return;
        }

        // feature reporting
        if (_currentNavType == NavType.Toc)
        {
            Context.FeatureReport.Report(FeatureKind.TocLinks, Location());
        }

        // The link of a 'toc', 'landmarks' or 'page-list' nav MUST resolve to a top-level content document. Links to
        // out-of-spine items of the container are already reported (RSC-011), so only the links to remote resources are.
        if (_currentNavType is NavType.Toc or NavType.PageList or NavType.Landmarks && Context.IsRemote(url))
        {
            Report.Message(MessageId.NAV_010, Location(), Text(_currentNavType), href);
        }

        // The links of a 'toc' and a 'page-list' nav are registered for the cross-reference checker, to check that they are in
        // reading order once all the content documents are read.
        else if (_currentNavType is NavType.Toc or NavType.PageList)
        {
            RegisterReference(url, _currentNavType == NavType.Toc ? ReferenceType.NavTocLink : ReferenceType.NavPagelistLink);
        }
    }

    /// <inheritdoc />
    protected override void EndElement()
    {
        base.EndElement();
        XMLElement e = CurrentElement;
        if (e.Namespace == Namespaces.Xhtml && e.Name == "nav")
        {
            _currentNavType = NavType.None;
        }
    }

    /// <inheritdoc />
    protected override void CheckType(string? type)
    {
        XMLElement e = CurrentElement;
        _isNavTypes = e.Namespace == Namespaces.Xhtml && e.Name == "nav";
        base.CheckType(type);
        _isNavTypes = false;
    }

    /// <inheritdoc />
    protected override void CheckTypes(IReadOnlySet<StructureVocab.EpubType> types)
    {
        base.CheckTypes(types);
        if (!_isNavTypes)
        {
            return;
        }

        if (types.Contains(StructureVocab.EpubType.Toc))
        {
            _currentNavType = NavType.Toc;
        }

        if (types.Contains(StructureVocab.EpubType.PageList))
        {
            _currentNavType = NavType.PageList;
            Context.FeatureReport.Report(FeatureKind.PageList, Location());
        }

        if (types.Contains(StructureVocab.EpubType.Landmarks))
        {
            _currentNavType = NavType.Landmarks;
        }

        if (types.Contains(StructureVocab.EpubType.Loi))
        {
            Context.FeatureReport.Report(FeatureKind.Loi, Location());
        }

        if (types.Contains(StructureVocab.EpubType.Lot))
        {
            Context.FeatureReport.Report(FeatureKind.Lot, Location());
        }

        if (types.Contains(StructureVocab.EpubType.Loa))
        {
            Context.FeatureReport.Report(FeatureKind.Loa, Location());
        }

        if (types.Contains(StructureVocab.EpubType.Lov))
        {
            Context.FeatureReport.Report(FeatureKind.Lov, Location());
        }
    }
}
