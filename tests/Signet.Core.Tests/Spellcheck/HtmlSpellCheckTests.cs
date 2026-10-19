using System.Linq;
using AwesomeAssertions;
using Signet.Core.Misc;
using Signet.Core.Spellcheck;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.Spellcheck;

/// <summary>
/// Tests for <see cref="HtmlSpellCheck"/> — extracting words while skipping tags/attributes/
/// <c>&lt;style&gt;</c>/<c>&lt;script&gt;</c>, choosing the dictionary per fragment by <c>lang</c>/<c>xml:lang</c>,
/// and integration with <see cref="SpellChecker"/> (Core only — Code Editor highlighting lives in App).
/// </summary>
public sealed class HtmlSpellCheckTests
{
    [Fact]
    public void GetAllWords_extracts_words_and_skips_tags_and_attributes()
    {
        // Arrange
        using TempDir dicts = new();
        using TempDir userDicts = new();
        DictionaryFixture.Write(dicts.Path, "en_US", "hello", "world");
        SettingsStore settings = NewSettings(dicts, "en_US");
        SpellChecker spellChecker = new(settings, dicts.Path, userDicts.Path);

        const string html = "<p class=\"greeting\" title=\"hello world\">hello world</p>";

        // Act
        HtmlWord[] words = HtmlSpellCheck.GetAllWords(spellChecker, settings, html).ToArray();

        // Assert
        words.Select(w => w.Text).Should().Equal("hello", "world");
        foreach (HtmlWord word in words)
        {
            html.Substring(word.Offset, word.Length).Should().Be(word.Text);
        }
    }

    [Fact]
    public void GetAllWords_skips_style_and_script_content()
    {
        // Arrange
        using TempDir dicts = new();
        using TempDir userDicts = new();
        DictionaryFixture.Write(dicts.Path, "en_US", "hello");
        SettingsStore settings = NewSettings(dicts, "en_US");
        SpellChecker spellChecker = new(settings, dicts.Path, userDicts.Path);

        const string html = "<style>body { color: red; }</style><p>hello</p><script>var x = notaword;</script>";

        // Act
        HtmlWord[] words = HtmlSpellCheck.GetAllWords(spellChecker, settings, html).ToArray();

        // Assert
        words.Select(w => w.Text).Should().Equal("hello");
    }

    [Fact]
    public void GetAllWords_keeps_an_underscore_between_word_characters_inside_the_word()
    {
        // Arrange
        using TempDir dicts = new();
        using TempDir userDicts = new();
        DictionaryFixture.Write(dicts.Path, "en_US", "hello", "world");
        SettingsStore settings = NewSettings(dicts, "en_US");
        SpellChecker spellChecker = new(settings, dicts.Path, userDicts.Path);

        // Act
        HtmlWord[] words = HtmlSpellCheck.GetAllWords(spellChecker, settings, "<p>snake_case _lead trail_ a__b</p>").ToArray();

        // Assert
        words.Select(w => w.Text).Should().Equal("snake_case", "lead", "trail", "a", "b");
    }

    [Theory]
    [InlineData("<p>hello <!-- a note --> world</p>")]
    [InlineData("<p><!-- a note -->hello world</p>")]
    [InlineData("<!-- a note --><p>hello</p><p>world</p>")]
    [InlineData("<p>hello<!---->world</p>")]
    public void GetAllWords_checks_the_words_after_an_html_comment(string html)
    {
        // Arrange
        using TempDir dicts = new();
        using TempDir userDicts = new();
        DictionaryFixture.Write(dicts.Path, "en_US", "hello", "world");
        SettingsStore settings = NewSettings(dicts, "en_US");
        SpellChecker spellChecker = new(settings, dicts.Path, userDicts.Path);

        // Act
        HtmlWord[] words = HtmlSpellCheck.GetAllWords(spellChecker, settings, html).ToArray();

        // Assert
        // calibre 5.38: the words following a comment were skipped.
        words.Select(w => w.Text).Should().EndWith("world");
        words.Select(w => w.Text).Should().NotContain("note", "the comment is not text of the book");
        foreach (HtmlWord word in words)
        {
            html.Substring(word.Offset, word.Length).Should().Be(word.Text);
        }
    }

    [Fact]
    public void GetMisspelledWords_uses_primary_dictionary_by_default()
    {
        // Arrange
        using TempDir dicts = new();
        using TempDir userDicts = new();
        DictionaryFixture.Write(dicts.Path, "en_US", "hello");
        SettingsStore settings = NewSettings(dicts, "en_US");
        SpellChecker spellChecker = new(settings, dicts.Path, userDicts.Path);

        const string html = "<p>hello wrold</p>";

        // Act
        HtmlWord[] misspelled = HtmlSpellCheck.GetMisspelledWords(spellChecker, settings, html).ToArray();

        // Assert
        misspelled.Select(w => w.Text).Should().Equal("wrold");
    }

    [Fact]
    public void GetMisspelledWords_uses_lang_attribute_to_switch_dictionary()
    {
        // Arrange
        using TempDir dicts = new();
        using TempDir userDicts = new();
        DictionaryFixture.Write(dicts.Path, "en_US", "hello");
        DictionaryFixture.Write(dicts.Path, "de_DE", "hund");
        SettingsStore settings = NewSettings(dicts, "en_US");
        SpellChecker spellChecker = new(settings, dicts.Path, userDicts.Path);

        const string html = "<p>hello</p><p lang=\"de\">hund katze</p>";

        // Act
        HtmlWord[] misspelled = HtmlSpellCheck.GetMisspelledWords(spellChecker, settings, html, defaultLang: "en").ToArray();

        // "hund" is in the German dictionary (lang="de" fragment) -> correct.
        // "katze" is in no dictionary -> misspelled, but resolved as German.
        // Assert
        misspelled.Should().ContainSingle(w => w.Text == "katze" && w.Lang == "de");
    }

    [Fact]
    public void GetAllWords_inherits_lang_from_ancestor_and_falls_back_to_default()
    {
        // Arrange
        using TempDir dicts = new();
        using TempDir userDicts = new();
        DictionaryFixture.Write(dicts.Path, "en_US", "hello");
        SettingsStore settings = NewSettings(dicts, "en_US");
        SpellChecker spellChecker = new(settings, dicts.Path, userDicts.Path);

        const string html = "<div lang=\"de\"><p>hund</p><p xml:lang=\"en\">hello</p></div><p>world</p>";

        // Act
        HtmlWord[] words = HtmlSpellCheck.GetAllWords(spellChecker, settings, html, defaultLang: "en").ToArray();

        // Assert
        words.Should().Contain(w => w.Text == "hund" && w.Lang == "de");
        words.Should().Contain(w => w.Text == "hello" && w.Lang == "en");
        words.Should().Contain(w => w.Text == "world" && w.Lang == "en");
    }

    private static SettingsStore NewSettings(TempDir dir, string primaryDictionary)
    {
        SettingsStore settings = new(dir.Combine("settings.xml"));
        settings.Dictionary = primaryDictionary;
        return settings;
    }
}
