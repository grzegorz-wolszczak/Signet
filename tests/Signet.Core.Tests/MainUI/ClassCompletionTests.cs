using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.MainUI;
using Xunit;

namespace Signet.Core.Tests.MainUI;

/// <summary>The suggestions of CSS class names in a <c>class</c> attribute: where, what is typed, what is left out, what is offered.</summary>
public sealed class ClassCompletionTests
{
    // The text with a "|" for the caret.
    private static (string Text, int Caret) Split(string marked)
    {
        int caret = marked.IndexOf('|', StringComparison.Ordinal);
        return (marked.Remove(caret, 1), caret);
    }

    private static ClassCompletionContext? Detect(string marked, CodeEditorSyntax syntax = CodeEditorSyntax.Html)
    {
        (string text, int caret) = Split(marked);
        return ClassCompletion.Detect(text, caret, syntax);
    }

    [Fact]
    public void The_word_typed_in_an_unfinished_tag_is_the_query()
    {
        // Arrange
        (string text, int caret) = Split("<body>\n<p class=\"fo|");

        // Act
        ClassCompletionContext? context = ClassCompletion.Detect(text, caret, CodeEditorSyntax.Html);

        // Assert
        context.Should().NotBeNull();
        context!.Query.Should().Be("fo");
        context.ReplaceStart.Should().Be(caret - 2);
        context.ReplaceEnd.Should().Be(caret);
        context.Used.Should().BeEmpty();
    }

    [Theory]
    [InlineData("<p class=\"note fo|\">", "fo", "note")]
    [InlineData("<p class='note fo|'>", "fo", "note")]
    [InlineData("<p class=\"note fo|\" id=\"x\">", "fo", "note")]
    [InlineData("<p class = \"note fo|\">", "fo", "note")]
    [InlineData("<p class=\"fo| note\">", "fo", "note")] // a class after the place is not offered either
    [InlineData("<p class=\"a b| c\">", "b", "a,c")]
    [InlineData("<p class=\"a b|c d\">", "b", "a,d")] // in the middle of a word: the word is the one typed
    [InlineData("<p class=\"a | b\">", "", "a,b")] // between words: nothing typed yet
    [InlineData("<p class=\"|\">", "", "")]
    [InlineData("<p class=\"|note\">", "", "")] // at the start of a word
    [InlineData("<p class=\"note|\">", "note", "")] // at the end of one: that word is the one being typed
    public void The_classes_the_attribute_has_are_not_offered_again_wherever_they_are(string marked, string query, string used)
    {
        // Act
        ClassCompletionContext? context = Detect(marked);

        // Assert
        context.Should().NotBeNull();
        context!.Query.Should().Be(query);
        context.Used.Should().BeEquivalentTo(used.Length == 0 ? new List<string>() : used.Split(',').ToList());
    }

    [Fact]
    public void The_replaced_text_is_the_whole_word_the_caret_is_in()
    {
        // Arrange
        (string text, int caret) = Split("<p class=\"a b|cd e\">");

        // Act
        ClassCompletionContext context = ClassCompletion.Detect(text, caret, CodeEditorSyntax.Html)!;

        // Assert
        text[context.ReplaceStart..context.ReplaceEnd].Should().Be("bcd");
        context.Query.Should().Be("b");
    }

    [Fact]
    public void Between_words_nothing_is_replaced()
    {
        // Arrange
        (string text, int caret) = Split("<p class=\"a | b\">");

        // Act
        ClassCompletionContext context = ClassCompletion.Detect(text, caret, CodeEditorSyntax.Html)!;

        // Assert
        context.ReplaceStart.Should().Be(caret);
        context.ReplaceEnd.Should().Be(caret);
    }

    [Theory]
    [InlineData("<p id=\"fo|\">")] // another attribute
    [InlineData("<p data-class=\"fo|\">")] // an attribute that only ends in "class"
    [InlineData("<p class=\"a\" title=\"fo|\">")]
    [InlineData("<p class=\"a\">fo|</p>")] // in the text
    [InlineData("<p class=\"a\"|>")] // after the closing quote
    [InlineData("class=\"fo|\"")] // not in a tag
    public void Outside_the_value_of_a_class_attribute_there_is_nothing_to_suggest(string marked)
    {
        // Act
        ClassCompletionContext? context = Detect(marked);

        // Assert
        context.Should().BeNull();
    }

    [Fact]
    public void Right_before_the_closing_quote_the_caret_is_still_in_the_value()
    {
        // Act
        ClassCompletionContext? context = Detect("<p class=\"a|\" >text");

        // Assert
        context.Should().NotBeNull();
        context!.Query.Should().Be("a");
    }

    [Theory]
    [InlineData(CodeEditorSyntax.Css)]
    [InlineData(CodeEditorSyntax.Xml)]
    [InlineData(CodeEditorSyntax.PlainText)]
    public void Only_html_has_class_suggestions(CodeEditorSyntax syntax)
    {
        // Act & Assert
        Detect("<p class=\"fo|\">", syntax).Should().BeNull();
    }

    [Fact]
    public void A_tag_that_started_long_ago_is_not_looked_for()
    {
        // Arrange
        string marked = "<p title=\"a\n\n\n\n\n\n\n\" class=\"fo|";

        // Act & Assert
        Detect(marked).Should().BeNull();
    }

    [Fact]
    public void The_classes_of_a_stylesheet_are_the_ones_its_selectors_name()
    {
        // Arrange
        const string css = "body { margin: 0 }\n.note, p.lead { color: red }\ndiv .box:hover > span.note { x: y }\n#id.tag { a: b }";

        // Act
        IReadOnlyList<string> classes = ClassCompletion.ClassesOfStylesheet(css);

        // Assert
        classes.Should().Equal("note", "lead", "box", "tag");
    }

    [Fact]
    public void The_classes_of_the_style_blocks_of_a_page_are_found_too()
    {
        // Arrange
        const string html = "<html><head><style>.a { x: y } .b { x: y }</style><style type=\"text/css\">p.c { x: y }</style></head><body/></html>";

        // Act
        IReadOnlyList<string> classes = ClassCompletion.ClassesOfStyleBlocks(html);

        // Assert
        classes.Should().Equal("a", "b", "c");
    }

    [Fact]
    public void Without_a_query_all_the_classes_not_used_are_offered_alphabetically_with_their_files()
    {
        // Arrange
        (string, string)[] defined = { ("zebra", "a.css"), ("Beta", "a.css"), ("alpha", "b.css"), ("alpha", "c.css"), ("used", "a.css") };

        // Act
        IReadOnlyList<LinkCompletionItem> items = ClassCompletion.Candidates(defined, new HashSet<string> { "used" }, string.Empty);

        // Assert
        items.Select(i => i.Text).Should().Equal("alpha", "Beta", "zebra");
        items[0].Description.Should().Be("b.css", "the first file that defines it");
    }

    [Fact]
    public void The_query_narrows_the_list_and_the_best_match_comes_first()
    {
        // Arrange
        (string, string)[] defined =
        {
            ("footnote-text", "a.css"), ("note", "a.css"), ("notebook", "a.css"), ("annotation", "a.css"), ("para", "a.css"),
        };

        // Act
        IReadOnlyList<LinkCompletionItem> items = ClassCompletion.Candidates(defined, new HashSet<string>(), "note");

        // Assert
        items[0].Text.Should().Be("note", "the exact match is the best");
        items.Select(i => i.Text).Should().Contain("footnote-text").And.Contain("notebook").And.NotContain("para");
    }

    [Fact]
    public void A_class_the_attribute_has_is_not_offered_even_when_it_matches_the_query_best()
    {
        // Arrange
        (string, string)[] defined = { ("note", "a.css"), ("notebook", "a.css") };

        // Act
        IReadOnlyList<LinkCompletionItem> items = ClassCompletion.Candidates(defined, new HashSet<string> { "note" }, "note");

        // Assert
        items.Select(i => i.Text).Should().Equal("notebook");
    }
}
