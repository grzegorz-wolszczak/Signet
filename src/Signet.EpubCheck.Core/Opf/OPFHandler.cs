using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Signet.EpubCheck.Core.Galimatias;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.References;
using Signet.EpubCheck.Core.Reporting;
using Signet.EpubCheck.Core.Util;
using Signet.EpubCheck.Core.Xml;

namespace Signet.EpubCheck.Core.Opf;

/// <summary>
/// Reads a package document (upstream's <c>OPFHandler</c>): the manifest items, the spine, the guide, the unique
/// identifier and the Dublin Core metadata, and reports what the schemas cannot (<c>OPF-047</c>…<c>OPF-085</c>,
/// <c>OPF-052</c>–<c>OPF-055</c>, <c>OPF-062</c>, <c>OPF-063</c>, <c>OPF-072</c>). The handler of EPUB 3 builds on it.
/// </summary>
public partial class OPFHandler : XmlHandler
{
    /// <summary>The key of the text collected in an element.</summary>
    protected const string TextKey = "text";

    private const string OpfNamespace = "http://www.idpf.org/2007/opf";
    private const string Oeb1Namespace = "http://openebook.org/namespaces/oeb-package/1.0/";
    private const string DcNamespace = "http://purl.org/dc/elements/1.1/";
    private const string UuidPrefix = "urn:uuid:";
    private const int UuidParts = 5;
    private const int MaxUuidLength = 36;
    private const string RolePrefix = "oth.";

    private static readonly HashSet<string> s_validRoles = new(StringComparer.Ordinal)
    {
        "abr", "acp", "act", "adi", "adp", "aft", "anl", "anm", "ann", "ant", "ape", "apl",
        "app", "aqt", "arc", "ard", "arr", "art", "asg", "asn", "ato", "att", "auc", "aud",
        "aui", "aus", "aut", "bdd", "bjd", "bkd", "bkp", "blw", "bnd", "bpd", "brd", "brl",
        "bsl", "cas", "ccp", "chr", "clb", "cli", "cll", "clr", "clt", "cmm", "cmp", "cmt",
        "cnd", "cng", "cns", "coe", "col", "com", "con", "cor", "cos", "cot", "cou", "cov",
        "cpc", "cpe", "cph", "cpl", "cpt", "cre", "crp", "crr", "crt", "csl", "csp", "cst",
        "ctb", "cte", "ctg", "ctr", "cts", "ctt", "cur", "cwt", "dbp", "dfd", "dfe", "dft",
        "dgc", "dgg", "dgs", "dis", "dln", "dnc", "dnr", "dpc", "dpt", "drm", "drt", "dsr",
        "dst", "dtc", "dte", "dtm", "dto", "dub", "edc", "edm", "edt", "egr", "elg", "elt",
        "eng", "enj", "etr", "evp", "exp", "fac", "fds", "fld", "flm", "fmd", "fmk", "fmo",
        "fmp", "fnd", "fpy", "frg", "gis", "grt", "his", "hnr", "hst", "ill", "ilu", "ins",
        "inv", "isb", "itr", "ive", "ivr", "jud", "jug", "lbr", "lbt", "ldr", "led", "lee",
        "lel", "len", "let", "lgd", "lie", "lil", "lit", "lsa", "lse", "lso", "ltg", "lyr",
        "mcp", "mdc", "med", "mfp", "mfr", "mod", "mon", "mrb", "mrk", "msd", "mte", "mtk",
        "mus", "nrt", "opn", "org", "orm", "osp", "oth", "own", "pad", "pan", "pat", "pbd",
        "pbl", "pdr", "pfr", "pht", "plt", "pma", "pmn", "pop", "ppm", "ppt", "pra", "prc",
        "prd", "pre", "prf", "prg", "prm", "prn", "pro", "prp", "prs", "prt", "prv", "pta",
        "pte", "ptf", "pth", "ptt", "pup", "rbr", "rcd", "rce", "rcp", "rdd", "red", "ren",
        "res", "rev", "rpc", "rps", "rpt", "rpy", "rse", "rsg", "rsp", "rsr", "rst", "rth",
        "rtm", "sad", "sce", "scl", "scr", "sds", "sec", "sgd", "sgn", "sht", "sll", "sng",
        "spk", "spn", "spy", "srv", "std", "stg", "stl", "stm", "stn", "str", "tcd", "tch",
        "ths", "tld", "tlp", "trc", "trl", "tyd", "tyg", "uvp", "vac", "vdg", "voc", "wac",
        "wal", "wam", "wat", "wdc", "wde", "win", "wit", "wpr", "wst",
    };

    private readonly List<string> _spineIds = [];
    private readonly List<OPFReference> _references = [];
    private OPFItems? _items;
    private string? _uniqueIdent;
    private bool _uniqueIdentExists;
    private int _spineItemCounter;
    private EpubLocation? _pageMapReferenceLocation;
    private string? _pageMapId;

    /// <summary>A handler of the package document of <paramref name="context"/>.</summary>
    public OPFHandler(ValidationContext context)
        : base(context)
    {
    }

    /// <summary>The item builders by id (trimmed), in manifest order.</summary>
    protected Dictionary<string, OPFItemBuilder> ItemBuilders { get; } = new(StringComparer.Ordinal);

    /// <summary>The item builders by URL.</summary>
    protected Dictionary<Url, OPFItemBuilder> ItemBuildersByUrl { get; } = [];

    /// <summary>The path of the package document.</summary>
    public string PackagePath => Path;

    /// <summary>Whether the package document is an OEB 1.2 package.</summary>
    public bool IsOpf12PackageFile { get; private set; }

    /// <summary>Whether the package document is an OPF 2.0 package.</summary>
    public bool IsOpf20PackageFile => !IsOpf12PackageFile;

    /// <summary>The text of the unique identifier, or <c>null</c>.</summary>
    public string? Uid { get; private set; }

    /// <summary>The value of the <c>unique-identifier</c> attribute (the id of the identifier element).</summary>
    public string? IdentId => _uniqueIdent;

    /// <summary>Whether a <c>dc:identifier</c> with the id of the <c>unique-identifier</c> was found.</summary>
    public bool CheckUniqueIdentExists() => _uniqueIdentExists;

    /// <summary>The number of entries of the guide.</summary>
    public int ReferenceCount => _references.Count;

    /// <summary>The entry <paramref name="index"/> of the guide.</summary>
    public OPFReference GetReference(int index) => _references[index];

    /// <summary>The item with the id <paramref name="id"/>, or <c>null</c>.</summary>
    public OPFItem? GetItemById(string id) => _items?.GetItemById(id);

    /// <summary>The item at <paramref name="url"/>, or <c>null</c>.</summary>
    public OPFItem? GetItemByUrl(Url url) => _items?.GetItemByUrl(url);

    /// <summary>The items of the spine.</summary>
    public IReadOnlyList<OPFItem> SpineItems => _items?.Spine ?? [];

    /// <summary>The items of the manifest.</summary>
    public IReadOnlyList<OPFItem> Items => _items?.Items ?? [];

    /// <inheritdoc />
    protected override void StartElement()
    {
        XMLElement element = CurrentElement;
        string ns = element.Namespace ?? string.Empty;
        if (ns.Length == 0 || ns == Oeb1Namespace || ns == OpfNamespace)
        {
            switch (element.Name)
            {
                case "package":
                    StartPackage(element, ns);
                    break;
                case "item":
                    StartItem(element);
                    break;
                case "reference":
                    StartReference(element);
                    break;
                case "spine":
                    StartSpine(element);
                    break;
                case "itemref":
                    StartItemRef(element);
                    break;
                case "dc-metadata" or "x-metadata":
                    if (!IsOpf12PackageFile)
                    {
                        Report.Message(MessageId.OPF_049, Location(), element.Name);
                    }

                    break;
            }
        }
        else if (ns == DcNamespace)
        {
            if (element.Name == "identifier")
            {
                string? id = element.GetAttribute("id");
                if (!string.IsNullOrEmpty(id) && id == _uniqueIdent)
                {
                    _uniqueIdentExists = true;
                }
            }
            else if (element.Name == "creator")
            {
                string? role = element.GetAttributeNS(OpfNamespace, "role");
                if (!string.IsNullOrEmpty(role) && !IsValidRole(role))
                {
                    Report.Message(MessageId.OPF_052, Location(), role);
                }
            }
        }
    }

    /// <inheritdoc />
    protected override void EndElement()
    {
        XMLElement element = CurrentElement;
        string ns = element.Namespace ?? string.Empty;
        if (ns.Length == 0 || ns == Oeb1Namespace || ns == OpfNamespace)
        {
            if (element.Name == "meta" && element.GetAttribute("property") == "dcterms:modified")
            {
                Report.Info(null, FeatureKind.ModifiedDate, (string?)element.GetPrivateData(TextKey));
            }
            else if (element.Name == "package")
            {
                BuildItems();
                if (_pageMapId is not null && GetItemById(_pageMapId) is null)
                {
                    Report.Message(MessageId.OPF_063, _pageMapReferenceLocation!, _pageMapId);
                }
            }
        }
        else if (ns == DcNamespace)
        {
            EndDublinCoreElement(element);
        }
    }

    /// <inheritdoc />
    public override void Characters(string text)
    {
        XMLElement? element = CurrentElement;
        if (element is null)
        {
            return;
        }

        bool keepValue = (element.Namespace == OpfNamespace && element.Name == "meta") || element.Namespace == DcNamespace;
        if (keepValue)
        {
            element.SetPrivateData(TextKey, (string?)element.GetPrivateData(TextKey) + text);
        }
    }

    /// <summary>Reports a manifest item, once the items are built.</summary>
    protected virtual void ReportItem(OPFItem item)
    {
        Report.Info(item.Path, FeatureKind.UniqueIdent, item.Id);
        if (item.IsInSpine)
        {
            Report.Info(item.Path, FeatureKind.IsSpineitem, "true");
            Report.Info(item.Path, FeatureKind.IsLinear, item.IsLinear ? "true" : "false");
        }

        if (item.IsNcx)
        {
            Report.Info(item.Path, FeatureKind.HasNcx, "true");
            if (item.MimeType != "application/x-dtbncx+xml")
            {
                Report.Message(MessageId.OPF_050, item.Location);
            }
        }
    }

    private static bool IsValidRole(string role) => s_validRoles.Contains(role) || role.StartsWith(RolePrefix, StringComparison.Ordinal);

    private void StartPackage(XMLElement element, string ns)
    {
        if (ns != OpfNamespace)
        {
            Report.Message(MessageId.OPF_047, Location());
            IsOpf12PackageFile = true;
        }

        string? unique = element.GetAttribute("unique-identifier");
        if (!string.IsNullOrEmpty(unique))
        {
            _uniqueIdent = unique;
        }
        else
        {
            Report.Message(MessageId.OPF_048, Location());
        }
    }

    private void StartItem(XMLElement element)
    {
        string? id = element.GetAttribute("id");
        string? href = element.GetAttribute("href");
        if (id is null || href is null || CheckUrl(href) is not { } url)
        {
            return; // checked by the schema
        }

        bool remote = Context.IsRemote(url);
        if (remote)
        {
            Report.Info(Path, FeatureKind.Reference, href);
        }

        OPFItemBuilder builder = new()
        {
            Id = id,
            Url = url,
            Location = Location(),
            Container = Context.Container,
            DocumentUrl = Context.Url,
            Remote = remote,
            MimeType = element.GetAttribute("media-type") ?? string.Empty,
            Fallback = element.GetAttribute("fallback") ?? string.Empty,
            FallbackStyle = element.GetAttribute("fallback-style") ?? string.Empty,
            MediaOverlay = element.GetAttribute("media-overlay"),
        };
        ItemBuilders[id.Trim()] = builder;
        ItemBuildersByUrl[url] = builder;
    }

    private void StartReference(XMLElement element)
    {
        string? href = element.GetAttribute("href");
        Url? url = CheckUrl(href);
        if (url is not null)
        {
            if (Context.IsRemote(url))
            {
                Report.Info(Path, FeatureKind.Reference, href);
            }

            RegisterReference(url, ReferenceType.Generic);
        }

        EpubLocation location = Location();
        _references.Add(new OPFReference(element.GetAttribute("type"), element.GetAttribute("title"), url, location.Line, location.Column));
    }

    private void StartSpine(XMLElement element)
    {
        string? pageMap = element.GetAttribute("page-map");
        if (pageMap is not null)
        {
            _pageMapId = pageMap;
            _pageMapReferenceLocation = Location().WithContext($"page-map=\"{_pageMapId}\"");
            Report.Message(MessageId.OPF_062, _pageMapReferenceLocation);
        }

        string? idref = element.GetAttribute("toc");
        if (idref is not null && ItemBuilders.TryGetValue(idref.Trim(), out OPFItemBuilder? toc))
        {
            toc.Ncx = true;
        }
        else
        {
            if (idref is not null)
            {
                Report.Message(MessageId.OPF_049, Location(), idref);
            }

            Report.Info(null, FeatureKind.HasNcx, "false");
        }
    }

    private void StartItemRef(XMLElement element)
    {
        string? idref = element.GetAttribute("idref");
        if (idref is null)
        {
            return;
        }

        if (ItemBuilders.TryGetValue(idref.Trim(), out OPFItemBuilder? item))
        {
            _spineIds.Add(idref.Trim());
            item.SpinePosition = _spineItemCounter++;
            if (element.GetAttribute("linear")?.Trim() == "no")
            {
                item.Linear = false;
            }
        }
        else
        {
            Report.Message(MessageId.OPF_049, Location(), idref);
        }
    }

    private void EndDublinCoreElement(XMLElement element)
    {
        string? text = (string?)element.GetPrivateData(TextKey);
        switch (element.Name)
        {
            case "identifier":
                EndIdentifier(element, text);
                break;
            case "date":
                EndDate(text);
                break;
            case "title" or "language":
                if (text is not null)
                {
                    Report.Info(null, element.Name == "language" ? FeatureKind.DcLanguage : FeatureKind.DcTitle, text.Trim());
                }

                if (Context.Version == EpubVersion.Version2 && (text is null || text.Trim().Length < 1))
                {
                    Report.Message(MessageId.OPF_055, Location(), element.Name);
                }

                break;
            default:
                string value = (text ?? string.Empty).Trim();
                if (Context.Version == EpubVersion.Version2 && value.Length == 0)
                {
                    Report.Message(MessageId.OPF_072, Location(), "dc:" + element.Name);
                }
                else if (value.Length > 0 && DublinCoreFeature(element.Name) is { } feature)
                {
                    Report.Info(null, feature, value);
                }

                break;
        }
    }

    private static FeatureKind? DublinCoreFeature(string name) => name switch
    {
        "creator" => FeatureKind.DcCreator,
        "contributor" => FeatureKind.DcContributor,
        "publisher" => FeatureKind.DcPublisher,
        "rights" => FeatureKind.DcRights,
        "subject" => FeatureKind.DcSubject,
        "description" => FeatureKind.DcDescription,
        _ => null,
    };

    private void EndIdentifier(XMLElement element, string? text)
    {
        string? id = element.GetAttribute("id");
        if (string.IsNullOrEmpty(id) || id.Trim() != _uniqueIdent || text is null)
        {
            return;
        }

        Uid = text.Trim();
        Report.Info(null, FeatureKind.UniqueIdent, Uid);
        Context.FeatureReport.Report(FeatureKind.UniqueIdent, Location(), Uid);
        string? scheme = element.GetAttributeNS(OpfNamespace, "scheme");
        if ((Uid.StartsWith(UuidPrefix, StringComparison.Ordinal) || string.Equals(scheme, "uuid", StringComparison.OrdinalIgnoreCase))
            && !IsUuid(Uid.Replace(UuidPrefix, string.Empty, StringComparison.Ordinal)))
        {
            Report.Message(MessageId.OPF_085, Location(), Uid);
        }
    }

    private void EndDate(string? text)
    {
        string? detail = null;
        if (string.IsNullOrEmpty(text))
        {
            detail = "zero-length string";
        }
        else
        {
            try
            {
                DateParser.Parse(text.Trim());
                Report.Info(null, FeatureKind.DcDate, text);
            }
            catch (InvalidDateException e)
            {
                detail = e.Message;
            }
        }

        if (detail is not null)
        {
            Report.Message(Context.Version == EpubVersion.Version3 ? MessageId.OPF_053 : MessageId.OPF_054, Location(), text ?? string.Empty, detail);
        }
    }

    // Java's UUID.fromString: five parts of hexadecimal digits (a trailing empty part is dropped by split), at most 36 characters.
    private static bool IsUuid(string value)
    {
        if (value.Length > MaxUuidLength)
        {
            return false;
        }

        List<string> parts = [.. value.Split('-')];
        while (parts.Count > 0 && parts[^1].Length == 0)
        {
            parts.RemoveAt(parts.Count - 1);
        }

        return parts.Count == UuidParts && parts.All(part => HexNumber().IsMatch(part));
    }

    private void BuildItems()
    {
        _items = OPFItems.Build(ItemBuilders, _spineIds, Context);
        foreach (OPFItem item in _items.Items)
        {
            ReportItem(item);
        }
    }

    [GeneratedRegex("\\A[0-9a-fA-F]{1,16}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex HexNumber();
}
