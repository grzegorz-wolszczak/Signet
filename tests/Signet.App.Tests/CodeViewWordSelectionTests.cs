using System;
using AvaloniaEdit.Document;
using AwesomeAssertions;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// The word a double click selects in Code View. AvaloniaEdit's <c>SelectionMouseHandler.GetWordAtMousePosition</c>
/// moves back to <see cref="CaretPositioningMode.WordStartOrSymbol"/> and then forward to
/// <see cref="CaretPositioningMode.WordBorderOrSymbol"/>; the tests repeat that on the document text. calibre 5.38 fixed
/// a double click that also selected the typographic quotes around the word.
/// </summary>
public sealed class CodeViewWordSelectionTests
{
    private static string WordAt(string text, int offset)
    {
        TextDocument document = new(text);
        int start = TextUtilities.GetNextCaretPosition(document, offset + 1, LogicalDirection.Backward, CaretPositioningMode.WordStartOrSymbol);
        start = start < 0 ? 0 : start;
        int end = TextUtilities.GetNextCaretPosition(document, start, LogicalDirection.Forward, CaretPositioningMode.WordBorderOrSymbol);
        end = end < 0 ? text.Length : end;
        return text[start..end];
    }

    [Theory]
    [InlineData("<p>“quoted”</p>")]
    [InlineData("<p>‘quoted’</p>")]
    [InlineData("<p>„quoted”</p>")]
    [InlineData("<p>«quoted»</p>")]
    public void A_double_click_selects_the_word_without_the_typographic_quotes(string text)
    {
        int inside = text.IndexOf("quoted", StringComparison.Ordinal) + 2;

        WordAt(text, inside).Should().Be("quoted");
    }
}
