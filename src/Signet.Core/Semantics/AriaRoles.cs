using System;
using System.Collections.Generic;
using System.Linq;
using Signet.Core.Localization;

namespace Signet.Core.Semantics;

/// <summary>
/// ARIA roles for documents (DPUB-ARIA + epub:type) — code, name, description, mapping to
/// <c>epub:type</c> and the list of allowed HTML elements.
/// The main table: <c>EmbeddedData/aria-roles.tsv</c>; the helper maps (epub:type, "raw" title,
/// allowed tags) are small and stay in code.
/// </summary>
/// <remarks>Names and descriptions are in the UI language (<c>CatalogText</c>); <see cref="GetTitle"/> returns the English name (book content).</remarks>
public static class AriaRoles
{
    private const string Catalog = "AriaRole";

    private static readonly IReadOnlyList<string[]> Data =
        ReferenceData.LoadTsv("ReferenceData.aria-roles", 3);

    private static readonly Dictionary<string, DescriptiveInfo> CodeMap =
        Data.ToDictionary(r => r[0], r => new DescriptiveInfo(r[1], r[2]), StringComparer.Ordinal);

    private static readonly Dictionary<string, string> NameMap =
        BuildNameMap(Data);

    // --- Tag lists ---------------------------------------- //
    private static readonly string[] RefTags = { "a" };
    private static readonly string[] NoteTags = { "aside", "header", "footer", "div", "p" };
    private static readonly string[] BreakTags = { "span", "hr" };
    private static readonly string[] H1H6Tags = { "h1", "h2", "h3", "h4", "h5", "h6" };
    private static readonly string[] SectionTags = { "section", "div" };
    private static readonly string[] SectionSideTags = { "section", "div", "aside", "p" };
    private static readonly string[] SideTags = { "aside", "div", "p" };
    private static readonly string[] NavTags = { "section", "nav", "div" };
    private static readonly string[] CvrTags = { "img" };
    private static readonly string[] EntryTags = { "li", "dt", "dd", "div", "p" };
    private static readonly string[] EntrySideTags = { "li", "dt", "dd", "aside", "div", "p" };

    private static readonly HashSet<string> NavRoles =
        new(new[] { "doc-index", "doc-pagelist", "doc-toc" }, StringComparer.Ordinal);

    private static readonly HashSet<string> RefRoles =
        new(new[] { "doc-backlink", "doc-biblioref", "doc-glossref", "doc-noteref" }, StringComparer.Ordinal);

    private static readonly HashSet<string> SectionSideRoles =
        new(new[] { "doc-dedication", "doc-example", "doc-glossary", "doc-pullquote" }, StringComparer.Ordinal);

    // --- Helper maps (epub:type / raw titles) --------- //
    private static readonly Dictionary<string, string> EpubTypeMap = new(StringComparer.Ordinal)
    {
        ["doc-abstract"] = "abstract",
        ["doc-acknowledgments"] = "acknowledgments",
        ["doc-afterword"] = "afterword",
        ["doc-appendix"] = "appendix",
        ["doc-backlink"] = "backlink",
        ["doc-bibliography"] = "bibliography",
        ["biblioentry"] = "biblioentry",
        ["doc-biblioref"] = "biblioref",
        ["doc-chapter"] = "chapter",
        ["doc-colophon"] = "colophon",
        ["doc-conclusion"] = "conclusion",
        ["doc-cover"] = "cover-image",
        ["doc-credit"] = "credit",
        ["doc-credits"] = "credits",
        ["doc-dedication"] = "dedication",
        ["endnote"] = "endnote",
        ["doc-endnotes"] = "endnotes",
        ["doc-epigraph"] = "epigraph",
        ["doc-epilogue"] = "epilogue",
        ["doc-errata"] = "errata",
        ["doc-example"] = string.Empty,
        ["doc-footnote"] = "footnote",
        ["footnotes"] = "footnotes",
        ["doc-foreword"] = "foreword",
        ["doc-glossary"] = "glossary",
        ["doc-glossref"] = "glossref",
        ["doc-index"] = "index",
        ["doc-introduction"] = "introduction",
        ["doc-noteref"] = "noteref",
        ["doc-notice"] = "notice",
        ["doc-pagebreak"] = "pagebreak",
        ["doc-pagelist"] = "page-list",
        ["doc-part"] = "part",
        ["preamble"] = "preamble",
        ["doc-preface"] = "preface",
        ["doc-prologue"] = "prologue",
        ["doc-pullquote"] = "pullquote",
        ["doc-qna"] = "qna",
        ["doc-subtitle"] = "subtitle",
        ["doc-tip"] = "tip",
        ["doc-toc"] = "toc",
    };

    private static readonly Dictionary<string, string> CodeToRawTitle = new(StringComparer.Ordinal)
    {
        ["doc-abstract"] = "Abstract",
        ["doc-acknowledgments"] = "Acknowledgments",
        ["doc-afterword"] = "Afterword",
        ["doc-appendix"] = "Appendix",
        ["doc-backlink"] = "Back Link",
        ["doc-bibliography"] = "Bibliography",
        ["biblioentry"] = "Bibliography Entry",
        ["doc-biblioref"] = "Bibliography Reference",
        ["doc-chapter"] = "Chapter",
        ["doc-colophon"] = "Colophon",
        ["doc-conclusion"] = "Conclusion",
        ["doc-cover"] = "Cover",
        ["doc-credit"] = "Credit",
        ["doc-credits"] = "Credits",
        ["doc-dedication"] = "Dedication",
        ["endnote"] = "Endnote",
        ["doc-endnotes"] = "Endnotes",
        ["doc-epigraph"] = "Epigraph",
        ["doc-epilogue"] = "Epilogue",
        ["doc-errata"] = "Errata",
        ["doc-example"] = "Example",
        ["doc-footnote"] = "Footnote",
        ["footnotes"] = "Footnotes",
        ["doc-foreword"] = "Foreword",
        ["doc-glossary"] = "Glossary",
        ["doc-glossref"] = "Glossary Reference",
        ["doc-index"] = "Index",
        ["doc-introduction"] = "Introduction",
        ["doc-noteref"] = "Note Reference",
        ["doc-notice"] = "Notice",
        ["doc-pagebreak"] = "Pagebreak",
        ["doc-pagefooter"] = "Page Footer",
        ["doc-pageheader"] = "Page Header",
        ["doc-pagelist"] = "Page List",
        ["doc-part"] = "Part",
        ["preamble"] = "Preamble",
        ["doc-preface"] = "Preface",
        ["doc-prologue"] = "Prologue",
        ["doc-pullquote"] = "Pull Quote",
        ["doc-qna"] = "Questions and Answers",
        ["doc-subtitle"] = "Subtitle",
        ["doc-tip"] = "Tip",
        ["doc-toc"] = "Table of Contents",
    };

    private static Dictionary<string, string> BuildNameMap(IReadOnlyList<string[]> data)
    {
        Dictionary<string, string> map = new(StringComparer.Ordinal);
        foreach (string[] row in data)
        {
            map[row[1]] = row[0];
        }

        return map;
    }

    /// <summary>The role name for a code; an empty string if the code is unknown.</summary>
    public static string GetName(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return CodeMap.TryGetValue(code, out DescriptiveInfo info) ? CatalogText.Name(Catalog, code, info.Name) : string.Empty;
    }

    /// <summary>
    /// The role title in the given language. Without i18n it returns the English name; for a code
    /// outside the "raw titles" map — the code itself. The <paramref name="lang"/> parameter is ignored.
    /// </summary>
    public static string GetTitle(string code, string lang)
    {
        ArgumentNullException.ThrowIfNull(code);
        _ = lang;
        return CodeToRawTitle.ContainsKey(code) && CodeMap.TryGetValue(code, out DescriptiveInfo info) ? info.Name : code;
    }

    /// <summary>The role description for a code; an empty string if the code is unknown.</summary>
    public static string GetDescriptionByCode(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return CodeMap.TryGetValue(code, out DescriptiveInfo info) ? CatalogText.Description(Catalog, code, info.Description) : string.Empty;
    }

    /// <summary>The role description for a name; an empty string if the name is unknown.</summary>
    public static string GetDescriptionByName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        string code = GetCode(name);
        return code.Length > 0 ? GetDescriptionByCode(code) : string.Empty;
    }

    /// <summary>The role code for a name; an empty string if the name is unknown.</summary>
    public static string GetCode(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (NameMap.TryGetValue(name, out string? code))
        {
            return code;
        }

        foreach (string candidate in CodeMap.Keys)
        {
            if (string.Equals(GetName(candidate), name, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        return string.Empty;
    }

    /// <summary>The alphabetically sorted list of role names.</summary>
    public static IReadOnlyList<string> GetSortedNames() =>
        CodeMap.Keys.Select(GetName).OrderBy(n => n, StringComparer.CurrentCulture).ToList();

    /// <summary>All role codes (in the order of the source data).</summary>
    public static IReadOnlyList<string> GetAllCodes() => Data.Select(r => r[0]).ToList();

    /// <summary>Whether the given string is a known role code.</summary>
    public static bool IsAriaRolesCode(string code) => code is not null && CodeMap.ContainsKey(code);

    /// <summary>Whether the given string is a known role name.</summary>
    public static bool IsAriaRolesName(string name) => name is not null && GetCode(name).Length > 0;

    /// <summary>A code -&gt; <see cref="DescriptiveInfo"/> map (read-only).</summary>
    public static IReadOnlyDictionary<string, DescriptiveInfo> GetCodeMap() =>
        CodeMap.Keys.ToDictionary(c => c, c => new DescriptiveInfo(GetName(c), GetDescriptionByCode(c)), StringComparer.Ordinal);

    /// <summary>
    /// The <c>epub:type</c> value corresponding to the code; an empty string if there is no mapping.
    /// </summary>
    public static string EpubTypeMapping(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return EpubTypeMap.GetValueOrDefault(code, string.Empty);
    }

    /// <summary>
    /// The list of HTML elements on which the given role is allowed.
    /// </summary>
    public static IReadOnlyList<string> AllowedTags(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (RefRoles.Contains(code))
        {
            return RefTags;
        }

        if (NavRoles.Contains(code))
        {
            return NavTags;
        }

        if (SectionSideRoles.Contains(code))
        {
            return SectionSideTags;
        }

        return code switch
        {
            "doc-footnote" => NoteTags,
            "doc-cover" => CvrTags,
            "doc-pagebreak" => BreakTags,
            "doc-subtitle" => H1H6Tags,
            "doc-tip" => SideTags,
            "biblioentry" => EntryTags,
            "footnotes" => SectionTags,
            "endnote" => EntrySideTags,
            "preamble" => SectionTags,
            _ => SectionTags,
        };
    }
}
