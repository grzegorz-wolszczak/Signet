using System;
using System.Collections.Generic;
using System.Linq;
using Signet.Core.Localization;

namespace Signet.Core.Semantics;

/// <summary>
/// The dictionary of EPUB 2 <c>&lt;guide&gt;</c> semantic codes (plus the extended <c>other.*</c> codes)
/// — name, description, "raw" title.
/// </summary>
/// <remarks>
/// Names and descriptions are localized through the catalog; <c>GetTitle(code, lang)</c> returns the English name.
/// </remarks>
public static class GuideItems
{
    private const string Catalog = "Guide";

    // name, code, description
    private static readonly (string Name, string Code, string Description)[] Data =
    {
        ("Acknowledgements", "acknowledgements", "A passage containing acknowledgments to entities involved in the realization of the work."),
        ("Afterword", "other.afterword", "A closing statement from the author or a person of importance to the story, typically providing insight into how the story came to be written, its significance or related events that have transpired since its timeline."),
        ("Appendix", "other.appendix", "Supplemental information."),
        ("Back Matter", "other.backmatter", "Ancillary material occurring after the main content of a publication, such as indices, appendices, etc."),
        ("Bibliography", "bibliography", "A list of works cited."),
        ("Text", "text", "The start of the main text content of a publication."),
        ("Colophon", "colophon", "A brief description usually located at the end of a publication, describing production notes relevant to the edition."),
        ("Conclusion", "other.conclusion", "An ending section that typically wraps up the work."),
        ("Contributors", "other.contributors", "A list of contributors to the work."),
        ("Copyright Page", "copyright-page", "The copyright page of the work."),
        ("Cover", "cover", "The publications cover(s), jacket information, etc."),
        ("Dedication", "dedication", "An inscription addressed to one or several particular person(s)."),
        ("Epilogue", "other.epilogue", "A concluding section that is typically written from a later point in time than the main story, although still part of the narrative."),
        ("Epigraph", "epigraph", "A quotation that is pertinent but not integral to the text."),
        ("Errata", "other.errata", "Publication errata, in printed works typically a loose sheet inserted by hand; sometimes a bound page that contains corrections for mistakes in the work."),
        ("Footnotes", "other.footnotes", "A collection of notes appearing at the bottom of a page."),
        ("Foreword", "foreword", "An introductory section that precedes the work, typically not written by the work's author."),
        ("Front Matter", "other.frontmatter", "Preliminary material to the main content of a publication, such as tables of contents, dedications, etc."),
        ("Glossary", "glossary", "An alphabetical list of terms in a particular domain of knowledge, with the definitions for those terms."),
        ("Half Title Page", "other.halftitlepage", "The half title page of the work which carries just the title itself."),
        ("Imprimatur", "other.imprimatur", "A formal statement authorizing the publication of the work."),
        ("Imprint", "other.imprint", "Information relating to the publication or distribution of the work."),
        ("Index", "index", "A detailed list, usually arranged alphabetically, of the specific information in a publication."),
        ("Introduction", "other.introduction", "A section in the beginning of the work, typically introducing the reader to the scope or nature of the work's content."),
        ("List of Illustrations", "loi", "A listing of illustrations included in the work."),
        ("List of Audio Clips", "other.loa", "A listing of audio clips included in the work."),
        ("List of Tables", "lot", "A listing of tables included in the work."),
        ("List of Video Clips", "other.lov", "A listing of video clips included in the work."),
        ("Notes", "notes", "A collection of notes. It can be used to identify footnotes, rear notes, marginal notes, inline notes, and similar when legacy naming conventions are not desired. Status: Deprecated - Replaced by: 'footnotes', 'rearnotes'"),
        ("Other Credits", "other.other-credits", "Acknowledgments of previously published parts of the work, illustration credits, and permission to quote from copyrighted material."),
        ("Preamble", "other.preamble", "A section in the beginning of the work, typically containing introductory and/or explanatory prose regarding the scope or nature of the work's content"),
        ("Preface", "preface", "An introductory section that precedes the work, typically written by the work's author."),
        ("Prologue", "other.prologue", "An introductory section that sets the background to a story, typically part of the narrative."),
        ("Rear Notes", "other.rearnotes", "A collection of notes appearing at the rear (backmatter) of the work, or at the end of a section."),
        ("Title Page", "title-page", "A page at the beginning of a book giving its title, authors, publisher and other publication information."),
        ("Table of Contents", "toc", "A table of contents which is a list of the headings or parts of the book or document, organized in the order in which they appear. Typically appearing in the work's frontmatter, or at the beginning of a section."),
    };

    private static readonly Dictionary<string, (string Name, string Description)> Source =
        Data.ToDictionary(e => e.Code, e => (e.Name, e.Description), StringComparer.Ordinal);

    private static readonly Dictionary<string, string> EnglishNameMap =
        Data.ToDictionary(e => e.Name, e => e.Code, StringComparer.Ordinal);

    /// <summary>The display name (in the UI language) for a code; for an unknown code — the code itself.</summary>
    public static string GetName(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return Source.TryGetValue(code, out var e) ? CatalogText.Name(Catalog, code, e.Name) : code;
    }

    /// <summary>
    /// The title for a code inserted into the <em>book content</em> (nav / guide) — the English name from the data,
    /// independent of the UI language; for an unknown code — the code itself. The <paramref name="lang"/> parameter
    /// (the book language) is currently ignored.
    /// </summary>
    public static string GetTitle(string code, string lang)
    {
        ArgumentNullException.ThrowIfNull(code);
        _ = lang;
        return Source.TryGetValue(code, out var e) && e.Name.Length > 0 ? e.Name : code;
    }

    /// <summary>The description (in the UI language) for a code; empty if unknown.</summary>
    public static string GetDescriptionByCode(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return Source.TryGetValue(code, out var e) ? CatalogText.Description(Catalog, code, e.Description) : string.Empty;
    }

    /// <summary>The description for a display name (English or translated); empty if unknown.</summary>
    public static string GetDescriptionByName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        string code = GetCode(name);
        return code.Length > 0 ? GetDescriptionByCode(code) : string.Empty;
    }

    /// <summary>The code for a display name (English or translated); empty if unknown.</summary>
    public static string GetCode(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (EnglishNameMap.TryGetValue(name, out string? code))
        {
            return code;
        }

        foreach ((string Name, string Code, string Description) e in Data)
        {
            if (string.Equals(GetName(e.Code), name, StringComparison.Ordinal))
            {
                return e.Code;
            }
        }

        return string.Empty;
    }

    /// <summary>Whether the given string is a known guide code.</summary>
    public static bool IsGuideItemsCode(string code) => code is not null && Source.ContainsKey(code);

    /// <summary>Whether the given string is a known guide name (English or translated).</summary>
    public static bool IsGuideItemsName(string name) => name is not null && GetCode(name).Length > 0;

    /// <summary>The sorted list of display names (in the UI language).</summary>
    public static IReadOnlyList<string> GetSortedNames() =>
        Data.Select(e => GetName(e.Code)).OrderBy(n => n, StringComparer.CurrentCulture).ToList();

    /// <summary>A code -&gt; <see cref="DescriptiveInfo"/> map with the name and description in the UI language.</summary>
    public static IReadOnlyDictionary<string, DescriptiveInfo> GetCodeMap() =>
        Data.ToDictionary(
            e => e.Code,
            e => new DescriptiveInfo(GetName(e.Code), GetDescriptionByCode(e.Code)),
            StringComparer.Ordinal);
}
