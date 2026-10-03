using System;
using System.Collections.Generic;
using System.Linq;
using Signet.Core.Localization;

namespace Signet.Core.Semantics;

/// <summary>
/// The dictionary of EPUB 3 <c>landmarks</c> (nav) semantic codes — name, description — and the
/// guide &#8596; landmarks mapping.
/// </summary>
/// <remarks>Names and descriptions are localized through the catalog (as in <see cref="GuideItems"/>).</remarks>
public static class Landmarks
{
    private const string Catalog = "Landmark";

    // name, code, description
    private static readonly (string Name, string Code, string Description)[] Data =
    {
        ("Acknowledgments", "acknowledgments", "A passage containing acknowledgments to entities involved in the realization of the work."),
        ("Afterword", "afterword", "A closing statement from the author or a person of importance to the story, typically providing insight into how the story came to be written, its significance or related events that have transpired since its timeline."),
        ("Annotation", "annotation", "Explanatory information about passages in the work. Status: Deprecated"),
        ("Appendix", "appendix", "Supplemental information."),
        ("Assessment", "assessment", "A test, quiz, or other activity that helps measure a student's understanding of what is being taught."),
        ("Back Matter", "backmatter", "Ancillary material occurring after the main content of a publication, such as indices, appendices, etc."),
        ("Bibliography", "bibliography", "A list of works cited."),
        ("Body Matter", "bodymatter", "The main content of a publication."),
        ("Chapter", "chapter", "A major structural division of a piece of writing."),
        ("Colophon", "colophon", "A brief description usually located at the end of a publication, describing production notes relevant to the edition."),
        ("Conclusion", "conclusion", "An ending section that typically wraps up the work."),
        ("Contributors", "contributors", "A list of contributors to the work."),
        ("Copyright Page", "copyright-page", "The copyright page of the work."),
        ("Cover", "cover", "The publications cover(s), jacket information, etc."),
        ("Dedication", "dedication", "An inscription addressed to one or several particular person(s)."),
        ("Division", "division", "A major structural division that may also appear as a substructure of a part (esp. in legislation)."),
        ("Endnotes", "endnotes", "A collection of notes at the end of a work or a section within it."),
        ("Epigraph", "epigraph", "A quotation that is pertinent but not integral to the text."),
        ("Epilogue", "epilogue", "A concluding section that is typically written from a later point in time than the main story, although still part of the narrative."),
        ("Errata", "errata", "Publication errata, in printed works typically a loose sheet inserted by hand; sometimes a bound page that contains corrections for mistakes in the work."),
        ("Footnotes", "footnotes", "A collection of notes appearing at the bottom of a page."),
        ("Foreword", "foreword", "An introductory section that precedes the work, typically not written by the work's author."),
        ("Front Matter", "frontmatter", "Preliminary material to the main content of a publication, such as tables of contents, dedications, etc."),
        ("Glossary", "glossary", "An alphabetical list of terms in a particular domain of knowledge, with the definitions for those terms."),
        ("Half Title Page", "halftitlepage", "The half title page of the work which carries just the title itself."),
        ("Imprimatur", "imprimatur", "A formal statement authorizing the publication of the work."),
        ("Imprint", "imprint", "Information relating to the publication or distribution of the work."),
        ("Index", "index", "A detailed list, usually arranged alphabetically, of the specific information in a publication."),
        ("Introduction", "introduction", "A section in the beginning of the work, typically introducing the reader to the scope or nature of the work's content."),
        ("Landmarks", "landmarks", "A collection of references to well-known/recurring components within the publication"),
        ("List of Audio Clips", "loa", "A listing of audio clips included in the work."),
        ("List of Illustrations", "loi", "A listing of illustrations included in the work."),
        ("List of Tables", "lot", "A listing of tables included in the work."),
        ("List of Video Clips", "lov", "A listing of video clips included in the work."),
        ("Notice", "notice", "Information that requires special attention, and that must not be skipped or suppressed. Examples include: alert, warning, caution, danger, important."),
        ("Other Credits", "other-credits", "Acknowledgments of previously published parts of the work, illustration credits, and permission to quote from copyrighted material."),
        ("Page List", "page-list", "A list of references to pagebreaks (start locations) from a print version of the ebook"),
        ("Part", "part", "A major structural division of a piece of writing, typically encapsulating a set of related chapters."),
        ("Preamble", "preamble", "A section in the beginning of the work, typically containing introductory and/or explanatory prose regarding the scope or nature of the work's content"),
        ("Preface", "preface", "An introductory section that precedes the work, typically written by the work's author."),
        ("Prologue", "prologue", "An introductory section that sets the background to a story, typically part of the narrative."),
        ("Questions and Answers", "qna", "A question and answer section."),
        ("Rear Notes", "rearnotes", "A collection of notes appearing at the rear (backmatter) of the work, or at the end of a section. Status: Deprecated"),
        ("Revision History", "revision-history", "A record of changes made to a work."),
        ("Subchapter", "subchapter", "A major sub-division of a chapter."),
        ("Title Page", "titlepage", "A page at the beginning of a book giving its title, authors, publisher and other publication information."),
        ("Table of Contents", "toc", "A table of contents which is a list of the headings or parts of the book or document, organized in the order in which they appear. Typically appearing in the work's frontmatter, or at the beginning of a section."),
        ("Volume", "volume", "A component of a collection."),
        ("Warning", "warning", "A warning or caution about specific material. Status: Deprecated - Replaced by 'notice'."),
    };

    private static readonly Dictionary<string, (string Name, string Description)> Source =
        Data.ToDictionary(e => e.Code, e => (e.Name, e.Description), StringComparer.Ordinal);

    private static readonly Dictionary<string, string> EnglishNameMap =
        Data.ToDictionary(e => e.Name, e => e.Code, StringComparer.Ordinal);

    private static readonly Dictionary<string, string> GuideLandMap = BuildGuideLandMap();

    /// <summary>The display name (in the UI language) for a code; for an unknown code — string.Empty.</summary>
    public static string GetName(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return Source.TryGetValue(code, out var e) ? CatalogText.Name(Catalog, code, e.Name) : string.Empty;
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

    /// <summary>Whether the given string is a known landmark code.</summary>
    public static bool IsLandmarksCode(string code) => code is not null && Source.ContainsKey(code);

    /// <summary>Whether the given string is a known landmark name (English or translated).</summary>
    public static bool IsLandmarksName(string name) => name is not null && GetCode(name).Length > 0;

    /// <summary>The sorted list of display names (in the UI language).</summary>
    public static IReadOnlyList<string> GetSortedNames() =>
        Data.Select(e => GetName(e.Code)).OrderBy(n => n, StringComparer.CurrentCulture).ToList();

    /// <summary>A code -&gt; <see cref="DescriptiveInfo"/> map with the name and description in the UI language.</summary>
    public static IReadOnlyDictionary<string, DescriptiveInfo> GetCodeMap() =>
        Data.ToDictionary(
            e => e.Code,
            e => new DescriptiveInfo(GetName(e.Code), GetDescriptionByCode(e.Code)),
            StringComparer.Ordinal);

    /// <summary>
    /// Maps a guide code to the corresponding landmark code (and vice versa), e.g.
    /// <c>text</c> &#8596; <c>bodymatter</c>, <c>title-page</c> &#8596; <c>titlepage</c>,
    /// <c>other.afterword</c> &#8596; <c>afterword</c>. An empty string if there is no mapping.
    /// </summary>
    public static string GuideLandMapping(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return GuideLandMap.GetValueOrDefault(code, string.Empty);
    }

    private static Dictionary<string, string> BuildGuideLandMap()
    {
        Dictionary<string, string> map = new(StringComparer.Ordinal)
        {
            ["acknowledgements"] = "acknowledgments",
            ["acknowledgments"] = "acknowledgements",
            ["bibliography"] = "bibliography",
            ["text"] = "bodymatter",
            ["bodymatter"] = "text",
            ["colophon"] = "colophon",
            ["copyright-page"] = "copyright-page",
            ["cover"] = "cover",
            ["dedication"] = "dedication",
            ["epigraph"] = "epigraph",
            ["foreword"] = "foreword",
            ["glossary"] = "glossary",
            ["index"] = "index",
            ["loi"] = "loi",
            ["lot"] = "lot",
            ["preface"] = "preface",
            ["title-page"] = "titlepage",
            ["titlepage"] = "title-page",
            ["toc"] = "toc",
        };

        // Extended other.* entries (guide -> landmark) and their inverse.
        string[] extended =
        {
            "afterword", "appendix", "backmatter", "conclusion", "contributors", "epilogue",
            "errata", "footnotes", "frontmatter", "halftitlepage", "imprimatur", "imprint",
            "introduction", "loa", "lov", "other-credits", "preamble", "prologue", "rearnotes", "endnotes",
        };
        foreach (string code in extended)
        {
            map["other." + code] = code;
            map[code] = "other." + code;
        }

        return map;
    }
}
