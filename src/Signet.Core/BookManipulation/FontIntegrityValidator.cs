using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Signet.Core.Fonts;
using Signet.Core.Parsers;
using Signet.Core.Resources;
using Signet.Core.Localization;

namespace Signet.Core.BookManipulation;

/// <summary>
/// Font checks: embeddability and name aliasing. Called from
/// <see cref="BookValidator.ValidateCurrentBook"/>, like
/// <see cref="OpfStructureValidator"/> and <see cref="LinkIntegrityValidator"/>.
/// </summary>
/// <remarks>
/// Checks two things: whether an embedded font has the embedding-restricting flag set in its
/// <c>OS/2.fsType</c> table (<see cref="OpenTypeFontInfo.IsEmbeddingRestricted"/>), and whether the
/// font family declared in the <c>@font-face</c> rule matches the actual name
/// stored in the font file's <c>name</c> table. Both checks only produce warnings — neither DRM
/// nor a name mismatch breaks the EPUB structure.
/// </remarks>
public static class FontIntegrityValidator
{
    /// <summary>Runs all the font checks for the whole book.</summary>
    public static IReadOnlyList<ValidationResult> Validate(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);

        List<ValidationResult> results = new();

        CheckCorruptFonts(book, results);
        CheckEmbeddingRestrictions(book, results);
        CheckFamilyAliasing(book, results);

        return results;
    }

    private static void CheckCorruptFonts(Book book, List<ValidationResult> results)
    {
        foreach (FontResource font in book.GetAllResources().OfType<FontResource>())
        {
            byte[] data;
            try
            {
                data = File.ReadAllBytes(font.FullPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (!IsIntact(data))
            {
                results.Add(new ValidationResult(
                    ValidationSeverity.Error, font.BookPath, -1, -1,
                    CoreStrings.Format("Validation_FontCorrupt", font.Filename), "Validation_FontCorrupt"));
            }
        }
    }

    /// <summary>
    /// Whether the font file looks complete: a WOFF/WOFF2 file is as long as its header says, an OpenType/TrueType
    /// file has a readable table directory and <c>name</c> table.
    /// </summary>
    internal static bool IsIntact(byte[] data)
    {
        if (data.Length >= 12)
        {
            uint signature = BinaryPrimitives.ReadUInt32BigEndian(data);
            if (signature is 0x774F4646 /* wOFF */ or 0x774F4632 /* wOF2 */)
            {
                return BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(8, 4)) == (uint)data.Length;
            }
        }

        try
        {
            return OpenTypeFontInfo.Parse(data) != FontFileInfo.Empty;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static void CheckEmbeddingRestrictions(Book book, List<ValidationResult> results)
    {
        foreach (FontResource font in book.GetAllResources().OfType<FontResource>())
        {
            FontFileInfo info = font.GetFontInfo();
            if (info.FsType is { } fsType && OpenTypeFontInfo.IsEmbeddingRestricted(fsType))
            {
                results.Add(new ValidationResult(
                    ValidationSeverity.Warning,
                    font.BookPath,
                    -1,
                    -1,
                    CoreStrings.Format("Validation_FontEmbeddingRestricted", font.BookPath, fsType),
                    "Validation_FontEmbeddingRestricted"));
            }
        }
    }

    private static void CheckFamilyAliasing(Book book, List<ValidationResult> results)
    {
        foreach (CssResource css in book.GetCssResources())
        {
            CssInfo cssInfo;
            try
            {
                cssInfo = new CssInfo(css.GetText());
            }
            catch (FormatException)
            {
                continue;
            }

            foreach (CssRule rule in cssInfo.Rules)
            {
                if (!string.Equals(rule.AtRulePrelude, "@font-face", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string? declaredFamily = null;
                string? srcReference = null;
                foreach (CssDeclaration decl in rule.Declarations)
                {
                    if (declaredFamily is null && string.Equals(decl.Property, "font-family", StringComparison.OrdinalIgnoreCase))
                    {
                        declaredFamily = Unquote(decl.Value);
                    }
                    else if (srcReference is null && string.Equals(decl.Property, "src", StringComparison.OrdinalIgnoreCase))
                    {
                        srcReference = ExtractUrl(decl.Value);
                    }
                }

                if (declaredFamily is null || declaredFamily.Length == 0 || srcReference is null || srcReference.Length == 0)
                {
                    continue;
                }

                string targetBookPath = Core.BookPath.BuildBookPath(Core.Utility.UrlDecodePath(srcReference), css.Folder);
                if (book.GetFolderKeeper().GetResourceByBookPathNoThrow(targetBookPath) is not FontResource font)
                {
                    continue;
                }

                string actualFamily = font.GetFontInfo().Family;
                if (actualFamily.Length == 0)
                {
                    continue;
                }

                if (!string.Equals(NormalizeFamilyName(declaredFamily), NormalizeFamilyName(actualFamily), StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(new ValidationResult(
                        ValidationSeverity.Warning,
                        css.BookPath,
                        -1,
                        -1,
                        CoreStrings.Format("Validation_FontFamilyMismatch", declaredFamily, actualFamily, targetBookPath),
                        "Validation_FontFamilyMismatch"));
                }
            }
        }
    }

    private static string Unquote(string value)
    {
        string trimmed = value.Trim();
        if (trimmed.Length >= 2 && (trimmed[0] == '"' || trimmed[0] == '\'') && trimmed[^1] == trimmed[0])
        {
            trimmed = trimmed[1..^1];
        }

        // "src" may contain several comma-separated sources (local()/url()) — font-family
        // may likewise contain a list of fallbacks; the first member is taken, as browsers do.
        int comma = trimmed.IndexOf(',', StringComparison.Ordinal);
        return (comma >= 0 ? trimmed[..comma] : trimmed).Trim();
    }

    private static string? ExtractUrl(string value)
    {
        int urlStart = value.IndexOf("url(", StringComparison.OrdinalIgnoreCase);
        if (urlStart < 0)
        {
            return null;
        }

        int contentStart = urlStart + 4;
        int end = value.IndexOf(')', contentStart);
        if (end < 0)
        {
            return null;
        }

        string raw = value[contentStart..end].Trim();
        if (raw.Length >= 2 && (raw[0] == '"' || raw[0] == '\'') && raw[^1] == raw[0])
        {
            raw = raw[1..^1];
        }

        int query = raw.IndexOf('?', StringComparison.Ordinal);
        if (query >= 0)
        {
            raw = raw[..query];
        }

        int hash = raw.IndexOf('#', StringComparison.Ordinal);
        return hash >= 0 ? raw[..hash] : raw;
    }

    private static string NormalizeFamilyName(string value) =>
        string.Concat(value.Where(c => !char.IsWhiteSpace(c)));
}
