using System.Collections.Generic;
using Signet.Core.Semantics;
using Signet.Core.Localization;

namespace Signet.Core.MainUI;

/// <summary>A single Unicode block/category (a subset of the official Unicode Blocks) for browsing in the "Insert Special Character" dialog.</summary>
/// <param name="Name">The block name shown to the user (the English Unicode block name).</param>
/// <param name="Start">The first code point of the block (inclusive).</param>
/// <param name="End">The last code point of the block (inclusive).</param>
public readonly record struct UnicodeBlock(string Name, int Start, int End)
{
    /// <summary>The block name in the interface language (the English <see cref="Name"/> when there is no translation).</summary>
    public string DisplayName =>
        CatalogText.Name("UnicodeBlock", Start.ToString("X4", System.Globalization.CultureInfo.InvariantCulture), Name);
}

/// <summary>
/// A static table of the Unicode blocks most useful when editing ebooks — a deliberately limited, useful
/// subset of the official "Unicode Blocks" (not the full Blocks.txt specification), plus a helper listing the assigned
/// code points of a given block using the existing name table
/// <see cref="CodepointNames"/>.
/// </summary>
public static class UnicodeBlocks
{
    /// <summary>The blocks in ascending order of their starting code point.</summary>
    public static IReadOnlyList<UnicodeBlock> Blocks { get; } = new UnicodeBlock[]
    {
        new("Basic Latin", 0x0000, 0x007F),
        new("Latin-1 Supplement", 0x0080, 0x00FF),
        new("Latin Extended-A", 0x0100, 0x017F),
        new("Latin Extended-B", 0x0180, 0x024F),
        new("IPA Extensions", 0x0250, 0x02AF),
        new("Spacing Modifier Letters", 0x02B0, 0x02FF),
        new("Combining Diacritical Marks", 0x0300, 0x036F),
        new("Greek and Coptic", 0x0370, 0x03FF),
        new("Cyrillic", 0x0400, 0x04FF),
        new("Cyrillic Supplement", 0x0500, 0x052F),
        new("Armenian", 0x0530, 0x058F),
        new("Hebrew", 0x0590, 0x05FF),
        new("Arabic", 0x0600, 0x06FF),
        new("Latin Extended Additional", 0x1E00, 0x1EFF),
        new("Greek Extended", 0x1F00, 0x1FFF),
        new("General Punctuation", 0x2000, 0x206F),
        new("Superscripts and Subscripts", 0x2070, 0x209F),
        new("Currency Symbols", 0x20A0, 0x20CF),
        new("Letterlike Symbols", 0x2100, 0x214F),
        new("Number Forms", 0x2150, 0x218F),
        new("Arrows", 0x2190, 0x21FF),
        new("Mathematical Operators", 0x2200, 0x22FF),
        new("Miscellaneous Technical", 0x2300, 0x23FF),
        new("Enclosed Alphanumerics", 0x2460, 0x24FF),
        new("Box Drawing", 0x2500, 0x257F),
        new("Block Elements", 0x2580, 0x259F),
        new("Geometric Shapes", 0x25A0, 0x25FF),
        new("Miscellaneous Symbols", 0x2600, 0x26FF),
        new("Dingbats", 0x2700, 0x27BF),
        new("CJK Symbols and Punctuation", 0x3000, 0x303F),
        new("Hiragana", 0x3040, 0x309F),
        new("Katakana", 0x30A0, 0x30FF),
    };

    /// <summary>
    /// The assigned code points of the block (ascending), truncated to <paramref name="maxResults"/> —
    /// unassigned positions in the range (<see cref="CodepointNames.GetName"/> returns <c>"Unknown"</c>)
    /// are skipped. Used to fill the character grid after a category is chosen in the dialog.
    /// </summary>
    public static IReadOnlyList<int> GetAssignedCodepoints(UnicodeBlock block, int maxResults = 500)
    {
        var result = new List<int>();
        for (int cp = block.Start; cp <= block.End && result.Count < maxResults; cp++)
        {
            if (cp <= 0x1F || CodepointNames.GetName(cp) != "Unknown")
            {
                result.Add(cp);
            }
        }

        return result;
    }
}
