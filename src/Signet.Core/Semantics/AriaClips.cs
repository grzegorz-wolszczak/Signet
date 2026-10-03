using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Signet.Core.Localization;

namespace Signet.Core.Semantics;

/// <summary>
/// Predefined ARIA/epub:type fragments (clips) inserted by the editor — code, name,
/// text template. Data: <c>EmbeddedData/aria-clips.json</c>.
/// The "description" in <see cref="DescriptiveInfo"/> = the template text.
/// </summary>
/// <remarks>
/// Clip names are in the UI language (<c>CatalogText</c>). <see cref="GetTitle"/>
/// returns the English name, and <see cref="TranslatePlaceholders"/> substitutes the default English labels
/// (the text inserted into the book).
/// </remarks>
public static class AriaClips
{
    private const string Catalog = "AriaClip";

    private static readonly List<ClipEntry> Entries = LoadEntries();

    private static readonly Dictionary<string, DescriptiveInfo> CodeMap =
        Entries.ToDictionary(e => e.Code, e => new DescriptiveInfo(e.Name, e.Template), StringComparer.Ordinal);

    private static readonly Dictionary<string, string> NameMap =
        BuildNameMap(Entries);

    private static readonly Dictionary<string, string> CodeToRawTitle = new(StringComparer.Ordinal)
    {
        ["section"] = "Section",
        ["aside"] = "Aside",
        ["chapter"] = "Chapter",
        ["footnotes"] = "Footnotes",
        ["fn ref"] = "Footnote reference",
        ["fn_backlink"] = "Backlink from Footnote",
        ["fn_aside"] = "Footnote in aside",
        ["fn_div"] = "Footnote in div",
        ["fn_p"] = "Footnote in p",
        ["endnote_ref"] = "Endnote reference",
        ["endnote_backlink"] = "Backlink from Endnote",
        ["endnotes"] = "Endnotes",
        ["endnote_li"] = "Endnote in li",
        ["sidebar"] = "Sidebar",
        ["tip"] = "Tip",
        ["pagebreak_hr"] = "Pagebreak in hr",
        ["pagebreak_span"] = "PageBreak in span",
    };

    private sealed record ClipEntry(string Code, string Name, string Template);

    private static List<ClipEntry> LoadEntries()
    {
        string json = ReferenceData.LoadText("ReferenceData.aria-clips");
        using JsonDocument doc = JsonDocument.Parse(json);
        List<ClipEntry> list = new();
        foreach (JsonElement el in doc.RootElement.EnumerateArray())
        {
            list.Add(new ClipEntry(
                el.GetProperty("code").GetString() ?? string.Empty,
                el.GetProperty("name").GetString() ?? string.Empty,
                el.GetProperty("template").GetString() ?? string.Empty));
        }

        return list;
    }

    private static Dictionary<string, string> BuildNameMap(List<ClipEntry> entries)
    {
        Dictionary<string, string> map = new(StringComparer.Ordinal);
        foreach (ClipEntry e in entries)
        {
            map[e.Name] = e.Code;
        }

        return map;
    }

    /// <summary>The clip name for a code; an empty string if the code is unknown.</summary>
    public static string GetName(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return CodeMap.TryGetValue(code, out DescriptiveInfo info) ? CatalogText.Name(Catalog, code, info.Name) : string.Empty;
    }

    /// <summary>
    /// The clip title in the given language. Without i18n it returns the English name; for a code outside the
    /// "raw titles" map — the code itself. The <paramref name="lang"/> parameter is ignored.
    /// </summary>
    public static string GetTitle(string code, string lang)
    {
        ArgumentNullException.ThrowIfNull(code);
        _ = lang;
        return CodeToRawTitle.ContainsKey(code) && CodeMap.TryGetValue(code, out DescriptiveInfo info) ? info.Name : code;
    }

    /// <summary>The clip text template for a code; an empty string if the code is unknown.</summary>
    public static string GetDescriptionByCode(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return CodeMap.TryGetValue(code, out DescriptiveInfo info) ? info.Description : string.Empty;
    }

    /// <summary>The clip text template for a name; an empty string if the name is unknown.</summary>
    public static string GetDescriptionByName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        string code = GetCode(name);
        return code.Length > 0 ? GetDescriptionByCode(code) : string.Empty;
    }

    /// <summary>The clip code for a name; an empty string if the name is unknown.</summary>
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

    /// <summary>The alphabetically sorted list of clip names.</summary>
    public static IReadOnlyList<string> GetSortedNames() =>
        CodeMap.Keys.Select(GetName).OrderBy(n => n, StringComparer.CurrentCulture).ToList();

    /// <summary>All clip codes (in the order of the source data).</summary>
    public static IReadOnlyList<string> GetAllCodes() => Entries.Select(e => e.Code).ToList();

    /// <summary>Whether the given string is a known clip code.</summary>
    public static bool IsAriaClipsCode(string code) => code is not null && CodeMap.ContainsKey(code);

    /// <summary>Whether the given string is a known clip name.</summary>
    public static bool IsAriaClipsName(string name) => name is not null && GetCode(name).Length > 0;

    /// <summary>A code -&gt; <see cref="DescriptiveInfo"/> map (description = template).</summary>
    public static IReadOnlyDictionary<string, DescriptiveInfo> GetCodeMap() =>
        CodeMap.Keys.ToDictionary(c => c, c => new DescriptiveInfo(GetName(c), GetDescriptionByCode(c)), StringComparer.Ordinal);

    /// <summary>
    /// Substitutes the placeholders (<c>CHAPTER_TITLE_HERE</c>, <c>LABEL_FOR_*</c> …) in the clip text
    /// with default (English) values — <paramref name="bookLang"/> is ignored.
    /// </summary>
    public static string TranslatePlaceholders(string clipText, string bookLang)
    {
        ArgumentNullException.ThrowIfNull(clipText);
        _ = bookLang;
        // CHAPTER_TITLE_HERE / HREF_TO_ENDNOTE / HREF_RETURN_FROM_ENDNOTE: their default (English)
        // value is the placeholder itself, so they are left unchanged.
        string text = clipText;
        text = text.Replace("LABEL_FOR_FOOTNOTES", "Footnotes", StringComparison.Ordinal);
        text = text.Replace("LABEL_FOR_TIP", "Tip", StringComparison.Ordinal);
        text = text.Replace("LABEL_FOR_SIDEBAR", "Sidebar", StringComparison.Ordinal);
        text = text.Replace("LABEL_FOR_PAGE", "Page", StringComparison.Ordinal);
        text = text.Replace("LABEL_FOR_FOOTNOTE_BACKLINK", "Back to", StringComparison.Ordinal);
        text = text.Replace("LABEL_FOR_ENDNOTE_BACKLINK", "Back to", StringComparison.Ordinal);
        text = text.Replace("LABEL_FOR_FOOTNOTE_REFERENCE", "To footnote", StringComparison.Ordinal);
        text = text.Replace("LABEL_FOR_ENDNOTE_REFERENCE", "To endnote", StringComparison.Ordinal);
        return text;
    }
}
