using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Signet.Core.Parsers;
using Signet.Core.Resources;

namespace Signet.Core.Fonts;

/// <summary>Why a font format may not display in every reading system.</summary>
public enum FontFormatWarning
{
    /// <summary>No concern.</summary>
    None,

    /// <summary>WOFF / WOFF2 in an EPUB 2 book — not among the formats EPUB 2 reading systems must support.</summary>
    NotInEpub2,

    /// <summary>WOFF2 in an EPUB 3 book — a newer format that older reading systems do not know.</summary>
    Woff2,
}

/// <summary>A font file about to get an <c>@font-face</c> rule: what it says about itself and what to warn about.</summary>
/// <param name="BookPath">The book path of the font file.</param>
/// <param name="FileName">The file name.</param>
/// <param name="Info">What the file says about itself (empty when it cannot be read).</param>
/// <param name="Suggested">The suggested descriptors (<see cref="FontFaceDescriptors.FromFont"/>).</param>
/// <param name="Generic">The generic family to fall back to (<see cref="FontFaceDescriptors.GenericFallback"/>).</param>
/// <param name="EmbeddingRestricted">Whether the font vendor restricts embedding (<c>OS/2.fsType</c>).</param>
/// <param name="FormatWarning">Whether the format may not display everywhere.</param>
public sealed record FontFaceCandidate(
    string BookPath,
    string FileName,
    FontFileInfo Info,
    FontFaceDescriptor Suggested,
    string Generic,
    bool EmbeddingRestricted,
    FontFormatWarning FormatWarning);

/// <summary>An <c>@font-face</c> rule to write.</summary>
/// <param name="FontBookPath">The font file the rule points to.</param>
/// <param name="Family">The family name.</param>
/// <param name="Weight">The <c>font-weight</c> value.</param>
/// <param name="Style">The <c>font-style</c> value.</param>
public sealed record FontFaceRequest(string FontBookPath, string Family, string Weight, string Style);

/// <summary>Why an <c>@font-face</c> rule is not needed.</summary>
public enum FontFaceConflictKind
{
    /// <summary>No conflict — the rule is written.</summary>
    None,

    /// <summary>A rule already points to the same font file.</summary>
    SameFile,

    /// <summary>A rule already describes the same face (family, weight and style) — in the book or earlier in the batch.</summary>
    SameFace,
}

/// <summary>A conflict of a requested rule with an existing one.</summary>
/// <param name="Kind">The kind of the conflict.</param>
/// <param name="ExistingRule">The existing rule, or <c>null</c> when the conflict is with an earlier request of the batch.</param>
/// <param name="EarlierFontBookPath">For a conflict within the batch: the font of the earlier request; otherwise <c>null</c>.</param>
public sealed record FontFaceConflict(FontFaceConflictKind Kind, FontFaceRule? ExistingRule, string? EarlierFontBookPath)
{
    /// <summary>No conflict.</summary>
    public static FontFaceConflict None { get; } = new(FontFaceConflictKind.None, null, null);
}

/// <summary>
/// Writing <c>@font-face</c> rules for font files: the suggested descriptors, the duplicate check, the rule text
/// and its place in the stylesheet (after the existing <c>@font-face</c> rules, or at the top after
/// <c>@charset</c>/<c>@import</c>), and optionally the font of <c>body</c>.
/// </summary>
public static class FontFaceAddition
{
    private const string DefaultIndent = "  ";

    private static readonly Regex IndentedDeclaration = new(@"^([ \t]+)[-A-Za-z]+\s*:", RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>Describes a font file of the book for the "add <c>@font-face</c>" dialog.</summary>
    public static FontFaceCandidate Describe(FontResource font, bool isEpub3)
    {
        ArgumentNullException.ThrowIfNull(font);
        FontFileInfo info = font.GetFontInfo();
        return Describe(font.BookPath, info, isEpub3);
    }

    /// <summary>Describes a font file from what it says about itself.</summary>
    public static FontFaceCandidate Describe(string fontBookPath, FontFileInfo info, bool isEpub3)
    {
        ArgumentNullException.ThrowIfNull(fontBookPath);
        string fileName = Path.GetFileName(fontBookPath);
        string extension = Path.GetExtension(fileName).ToLowerInvariant();
        bool woff2 = info.Format == FontFileFormat.Woff2 || extension == ".woff2";
        bool woff = woff2 || info.Format == FontFileFormat.Woff || extension == ".woff";
        FontFormatWarning warning = !isEpub3 && woff ? FontFormatWarning.NotInEpub2
            : isEpub3 && woff2 ? FontFormatWarning.Woff2
            : FontFormatWarning.None;
        return new FontFaceCandidate(
            fontBookPath,
            fileName,
            info,
            FontFaceDescriptors.FromFont(info, fileName),
            FontFaceDescriptors.GenericFallback(info),
            info.FsType is { } fsType && OpenTypeFontInfo.IsEmbeddingRestricted(fsType),
            warning);
    }

    /// <summary>
    /// Whether <paramref name="request"/> is already covered: a rule of the book points to the same file, or a rule —
    /// of the book or of <paramref name="earlierInBatch"/> — describes the same face.
    /// </summary>
    public static FontFaceConflict FindConflict(BookFontIndex index, FontFaceRequest request, IEnumerable<FontFaceRequest> earlierInBatch)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(earlierInBatch);
        if (index.RulesForFont(request.FontBookPath).FirstOrDefault() is { } sameFile)
        {
            return new FontFaceConflict(FontFaceConflictKind.SameFile, sameFile, null);
        }

        if (index.Rules.FirstOrDefault(r => r.IsSameFace(request.Family, request.Weight, request.Style)) is { } sameFace)
        {
            return new FontFaceConflict(FontFaceConflictKind.SameFace, sameFace, null);
        }

        foreach (FontFaceRequest earlier in earlierInBatch)
        {
            if (string.Equals(FontFaceDescriptors.FamilyKey(earlier.Family), FontFaceDescriptors.FamilyKey(request.Family), StringComparison.Ordinal)
                && FontFaceDescriptors.ParseWeight(earlier.Weight) == FontFaceDescriptors.ParseWeight(request.Weight)
                && FontFaceDescriptors.NormalizeStyle(earlier.Style) == FontFaceDescriptors.NormalizeStyle(request.Style))
            {
                return new FontFaceConflict(FontFaceConflictKind.SameFace, null, earlier.FontBookPath);
            }
        }

        return FontFaceConflict.None;
    }

    /// <summary>
    /// The new text of the stylesheet <paramref name="cssBookPath"/> with the rules of <paramref name="requests"/>
    /// added and — when <paramref name="bodyFamily"/> is given — <c>body</c> set to that family followed by
    /// <paramref name="bodyGeneric"/> (an existing top-level <c>body</c> rule is changed, otherwise one is added after
    /// the new rules). The indentation follows the stylesheet (two spaces when it has none).
    /// </summary>
    public static string Apply(
        string cssText,
        string cssBookPath,
        IReadOnlyList<FontFaceRequest> requests,
        string? bodyFamily = null,
        string bodyGeneric = "serif")
    {
        ArgumentNullException.ThrowIfNull(cssText);
        ArgumentNullException.ThrowIfNull(cssBookPath);
        ArgumentNullException.ThrowIfNull(requests);
        string indent = DetectIndent(cssText);
        List<string> blocks = requests.Select(r => BuildRule(r, cssBookPath, indent)).ToList();

        string text = cssText;
        if (!string.IsNullOrWhiteSpace(bodyFamily))
        {
            string familyValue = string.Concat(FontFamilyList.Quote(bodyFamily.Trim()), ", ", bodyGeneric);
            if (SetBodyFamily(text, familyValue, indent) is { } changed)
            {
                text = changed;
            }
            else
            {
                blocks.Add(string.Concat("body {\n", indent, "font-family: ", familyValue, ";\n}"));
            }
        }

        return blocks.Count == 0 ? text : InsertBlocks(text, string.Join("\n\n", blocks));
    }

    /// <summary>The text of an <c>@font-face</c> rule, with a URL relative to the stylesheet.</summary>
    public static string BuildRule(FontFaceRequest request, string cssBookPath, string indent = DefaultIndent)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(cssBookPath);
        string url = Utility.UrlEncodePath(BookPath.Relative(cssBookPath, request.FontBookPath));
        StringBuilder sb = new();
        sb.Append("@font-face {\n");
        sb.Append(indent).Append("font-family: ").Append(FontFamilyList.Quote(request.Family.Trim())).Append(";\n");
        sb.Append(indent).Append("font-style: ").Append(request.Style.Trim()).Append(";\n");
        sb.Append(indent).Append("font-weight: ").Append(request.Weight.Trim()).Append(";\n");
        sb.Append(indent).Append("src: url(\"").Append(url).Append("\");\n");
        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>The indentation of the first indented declaration of the stylesheet (two spaces when there is none).</summary>
    public static string DetectIndent(string cssText)
    {
        ArgumentNullException.ThrowIfNull(cssText);
        Match match = IndentedDeclaration.Match(cssText);
        return match.Success ? match.Groups[1].Value : DefaultIndent;
    }

    // Inserts the block after the last top-level @font-face rule, or at the top after @charset/@import/@namespace,
    // with one blank line around it.
    private static string InsertBlocks(string text, string block)
    {
        CssScannedRule? lastFontFace = CssDeclarationScanner.ScanRules(text)
            .LastOrDefault(r => r.IsFontFace && r.ParentAtRule is null);
        int position = lastFontFace?.End ?? AfterLeadingStatements(text);
        string before = text[..position].TrimEnd();
        string after = text[position..].TrimStart();
        return string.Concat(
            before.Length > 0 ? before + "\n\n" : string.Empty,
            block,
            after.Length > 0 ? "\n\n" + after : "\n");
    }

    // The offset after the last @charset / @import / @namespace statement at the top of the stylesheet (0 without them).
    private static int AfterLeadingStatements(string text)
    {
        int position = 0;
        int pos = CssDeclarationScanner.SkipWhitespaceAndComments(text, 0, text.Length);
        while (pos < text.Length && text[pos] == '@'
               && (StartsWithWord(text, pos, "@charset") || StartsWithWord(text, pos, "@import") || StartsWithWord(text, pos, "@namespace")))
        {
            int semicolon = text.IndexOf(';', pos);
            if (semicolon < 0)
            {
                break;
            }

            position = semicolon + 1;
            pos = CssDeclarationScanner.SkipWhitespaceAndComments(text, position, text.Length);
        }

        return position;
    }

    private static bool StartsWithWord(string text, int pos, string word) =>
        string.Compare(text, pos, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) == 0;

    // Sets font-family in the first top-level "body" rule; null when the stylesheet has no such rule.
    private static string? SetBodyFamily(string text, string familyValue, string indent)
    {
        CssScannedRule? body = CssDeclarationScanner.ScanRules(text)
            .FirstOrDefault(r => r.ParentAtRule is null && !r.IsAtRule
                                 && string.Equals(r.Prelude, "body", StringComparison.OrdinalIgnoreCase));
        if (body is null)
        {
            return null;
        }

        CssScannedDeclaration? existing = body.Declarations
            .LastOrDefault(d => string.Equals(d.Property, "font-family", StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            string old = text[existing.ValueStart..existing.ValueEnd];
            Match important = Regex.Match(old, @"\s*!\s*important\s*$", RegexOptions.IgnoreCase);
            string value = important.Success ? familyValue + important.Value : familyValue;
            return string.Concat(text[..existing.ValueStart], value, text[existing.ValueEnd..]);
        }

        int closeBrace = body.End - 1;
        if (closeBrace < 0 || closeBrace >= text.Length || text[closeBrace] != '}')
        {
            return null;
        }

        StringBuilder sb = new(text.Length + familyValue.Length + 32);
        CssScannedDeclaration? last = body.Declarations.Count > 0 ? body.Declarations[^1] : null;
        int lineStart = text.LastIndexOf('\n', closeBrace - 1 < 0 ? 0 : closeBrace - 1) + 1;
        bool braceOnOwnLine = lineStart > body.BlockStart && string.IsNullOrWhiteSpace(text[lineStart..closeBrace]);
        if (last is not null && last.End == last.ValueEnd)
        {
            // The last declaration has no semicolon — it gets one before the new declaration.
            sb.Append(text, 0, last.ValueEnd).Append(';');
            sb.Append(text, last.ValueEnd, (braceOnOwnLine ? lineStart : closeBrace) - last.ValueEnd);
        }
        else
        {
            sb.Append(text, 0, braceOnOwnLine ? lineStart : closeBrace);
        }

        if (braceOnOwnLine)
        {
            sb.Append(indent).Append("font-family: ").Append(familyValue).Append(";\n");
            sb.Append(text, lineStart, text.Length - lineStart);
        }
        else
        {
            string beforeBrace = sb.ToString();
            sb.Clear().Append(beforeBrace.TrimEnd()).Append(' ').Append("font-family: ").Append(familyValue).Append("; ");
            sb.Append(text, closeBrace, text.Length - closeBrace);
        }

        return sb.ToString();
    }
}
