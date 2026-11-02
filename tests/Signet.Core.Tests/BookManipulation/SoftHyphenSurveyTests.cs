using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>
/// Tests of <see cref="SoftHyphenSurvey"/> and of the soft hyphen operations of <see cref="Book"/>, on the corpus book
/// <c>broken/languages</c> (several languages, said in several ways, and soft hyphens outside the text — see
/// <c>tests/corpus/broken/README.md</c>) and on single pages.
/// </summary>
public sealed class SoftHyphenSurveyTests
{
    private const string Shy = SoftHyphenInserter.SoftHyphenEntity;

    private static Book LoadLanguages(TempDir temp) =>
        new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Broken("languages"), temp)).GetBook();

    private static HtmlResource File(Book book, string name) => book.GetHtmlResources().Single(h => h.Filename == name);

    private static SoftHyphenSurvey Survey(string body, string defaultLanguage = "") =>
        SoftHyphenSurvey.Of(
            [("Text/page.xhtml", "<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>T</title></head><body>" + body + "</body></html>")],
            defaultLanguage);

    [Fact]
    public void The_languages_of_the_book_are_listed_in_the_order_they_appear_with_where_each_is_said()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = LoadLanguages(temp);

        // Act
        SoftHyphenSurvey sut = book.SurveySoftHyphens();

        // Assert
        sut.Languages.Select(l => l.Language).Should().Equal("pl", "en", "de", "en-GB", "EN-us");
        SoftHyphenLanguage polish = sut.Languages.Single(l => l.Key == "pl");
        polish.Files.Select(f => f.BookPath).Should().Equal(File(book, "languages.xhtml").BookPath, File(book, "outside.xhtml").BookPath);
        polish.Sources.Should().Equal(
            (new LanguageSource(LanguageSourceKind.Element, "html"), 1),
            (LanguageSource.Opf, 1));
        sut.Languages.Single(l => l.Key == "en").Sources.Select(s => s.Source.ElementName).Should().Equal("html", "span");
        sut.Languages.Single(l => l.Key == "de").Sources.Single().Source.ElementName.Should().Be("blockquote");
    }

    [Fact]
    public void An_element_stating_a_language_around_white_space_only_adds_no_language()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = LoadLanguages(temp);

        // Act
        SoftHyphenSurvey sut = book.SurveySoftHyphens();

        // Assert
        sut.Languages.Should().NotContain(l => l.Key == "fr");
    }

    [Fact]
    public void The_soft_hyphens_outside_the_text_are_listed_one_by_one_with_where_they_stand()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = LoadLanguages(temp);
        string outside = File(book, "outside.xhtml").BookPath;

        // Act
        SoftHyphenSurvey sut = book.SurveySoftHyphens();

        // Assert
        sut.OutsideText.Should().OnlyContain(p => p.BookPath == outside);
        sut.OutsideText.Select(p => (p.Kind, p.Name)).Should().Equal(
            (SoftHyphenPlaceKind.Head, "head"),
            (SoftHyphenPlaceKind.Attribute, "href"),
            (SoftHyphenPlaceKind.Attribute, "id"),
            (SoftHyphenPlaceKind.Attribute, "title"),
            (SoftHyphenPlaceKind.Element, "pre"),
            (SoftHyphenPlaceKind.Element, "code"),
            (SoftHyphenPlaceKind.Comment, string.Empty));
        SoftHyphenPlace href = sut.OutsideText[1];
        href.Context.Substring(href.ContextMatchStart, href.ContextMatchLength).Should().Be(Shy);
        File(book, "outside.xhtml").GetText().Split('\n')[href.Line - 1].Should().Contain("za" + Shy + "kotwiczenie");
    }

    [Fact]
    public void The_soft_hyphens_of_the_text_are_counted_under_its_language()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = LoadLanguages(temp);

        // Act
        SoftHyphenSurvey sut = book.SurveySoftHyphens();

        // Assert
        sut.Languages.Single(l => l.Key == "pl").SoftHyphens.Should().Be(1, "\"podzie-lone\" is hyphenated already");
        sut.HasSoftHyphens.Should().BeTrue();
    }

    [Fact]
    public void Words_to_hyphenate_are_the_long_ones_without_a_soft_hyphen()
    {
        // Act
        SoftHyphenSurvey sut = Survey($"<p lang=\"en\">a abcdefgh abcdefghij abcd{Shy}efgh</p>");

        // Assert
        SoftHyphenLanguage english = sut.Languages.Single();
        english.WordsToHyphenate(8).Should().Be(2);
        english.WordsToHyphenate(9).Should().Be(1);
        english.WordsToHyphenate(1).Should().Be(3);
        english.SoftHyphens.Should().Be(1);
    }

    [Fact]
    public void Text_without_a_stated_language_in_a_book_without_one_is_in_the_unknown_language_listed_last()
    {
        // Act
        SoftHyphenSurvey sut = Survey("<p>unknown</p><p lang=\"pl\">znany</p>");

        // Assert
        sut.Languages.Select(l => l.Key).Should().Equal("pl", string.Empty);
        SoftHyphenLanguage unknown = sut.Languages[^1];
        unknown.IsUnknown.Should().BeTrue();
        unknown.Sources.Single().Source.Should().Be(LanguageSource.None);
    }

    [Fact]
    public void Languages_written_differently_are_one_language_shown_as_first_written()
    {
        // Act
        SoftHyphenSurvey sut = Survey("<p lang=\"en_GB\">one</p><p lang=\"EN-gb\">two</p>");

        // Assert
        sut.Languages.Should().ContainSingle().Which.Language.Should().Be("en-GB");
    }

    [Fact]
    public void Adding_hyphenates_the_chosen_languages_by_their_patterns_and_leaves_the_rest()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = LoadLanguages(temp);
        HtmlResource languages = File(book, "languages.xhtml");
        string before = languages.GetText();
        SoftHyphenAddPlan plan = SoftHyphenAddPlan.ForEveryLanguageWithPatterns(
            book.SurveySoftHyphens(), TestHyphenation.EnglishAndPolish, form: SoftHyphenForm.Reference);

        // Act
        FileBatchResult result = book.AddSoftHyphens(plan);

        // Assert
        result.HasSkipped.Should().BeFalse();
        string after = languages.GetText();
        after.Should().Contain($"cierpliwoś{Shy}ci", "Polish is broken by its patterns");
        after.Should().Contain("Unabhängigkeit erfordert Geduld", "German has no patterns");
        after.Should().Contain("Colourful neighbourhoods everywhere", "en-GB is not among the chosen languages");
        after.Should().Contain($"podzie{Shy}lone", "a word hyphenated already is left as it is");
        SoftHyphenInserter.RemoveSoftHyphens(after, "pl", _ => true, new HashSet<int>())
            .Should().Be(SoftHyphenInserter.RemoveSoftHyphens(before, "pl", _ => true, new HashSet<int>()), "only soft hyphens were added");
        File(book, "outside.xhtml").GetText().Should().Contain($"<pre>kod{Shy}źródłowy</pre>", "not prose");
    }

    [Fact]
    public void Adding_touches_only_the_files_chosen()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = LoadLanguages(temp);
        HtmlResource languages = File(book, "languages.xhtml");
        string before = languages.GetText();
        SoftHyphenAddPlan plan = new(
            new HashSet<(string, string)> { (File(book, "outside.xhtml").BookPath, "pl") },
            TestHyphenation.EnglishAndPolish);

        // Act
        book.AddSoftHyphens(plan);

        // Assert
        languages.GetText().Should().Be(before);
    }

    [Fact]
    public void The_soft_hyphens_are_counted_by_the_form_they_are_written_in()
    {
        // Act
        SoftHyphenSurvey sut = Survey("<p lang=\"en\">a&shy;b&#173;c\u00ADd&#xAD;e</p><p lang=\"en\" title=\"x\u00ADy\">z</p>");

        // Assert
        SoftHyphenLanguage english = sut.Languages.Single();
        english.SoftHyphens.Should().Be(4);
        english.SoftHyphensNotIn(SoftHyphenForm.Character).Should().Be(3);
        english.SoftHyphensNotIn(SoftHyphenForm.Reference).Should().Be(3);
        sut.OutsideText.Single().IsIn(SoftHyphenForm.Character).Should().BeTrue();
        sut.OutsideText.Single().Text.Should().Be("\u00AD");
    }

    [Fact]
    public void Converting_everything_writes_every_soft_hyphen_of_the_book_in_one_form()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = LoadLanguages(temp);

        // Act
        FileBatchResult result = book.ConvertSoftHyphens(SoftHyphenConvertPlan.Everything(book.SurveySoftHyphens(), SoftHyphenForm.Character));

        // Assert
        result.Modified.Should().BeTrue();
        SoftHyphenSurvey after = book.SurveySoftHyphens();
        after.Languages.Should().OnlyContain(l => l.SoftHyphensNotIn(SoftHyphenForm.Character) == 0);
        after.OutsideText.Should().OnlyContain(p => p.IsIn(SoftHyphenForm.Character));
        after.OutsideText.Should().HaveCount(7, "converting keeps every soft hyphen");
        File(book, "outside.xhtml").GetText().Should().Contain("<pre>kod\u00ADźródłowy</pre>");
    }

    [Fact]
    public void Removing_everything_takes_every_soft_hyphen_of_the_book()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = LoadLanguages(temp);

        // Act
        FileBatchResult result = book.RemoveSoftHyphens(SoftHyphenRemovePlan.Everything(book.SurveySoftHyphens()));

        // Assert
        result.Modified.Should().BeTrue();
        book.SurveySoftHyphens().HasSoftHyphens.Should().BeFalse();
        File(book, "outside.xhtml").GetText().Should().Contain("href=\"languages.xhtml#zakotwiczenie\"", "the broken link is mended");
    }

    [Fact]
    public void Removing_takes_only_the_chosen_languages_and_places()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = LoadLanguages(temp);
        SoftHyphenSurvey survey = book.SurveySoftHyphens();
        SoftHyphenPlace href = survey.OutsideText.Single(p => p.Name == "href");
        SoftHyphenRemovePlan plan = new(new HashSet<(string, string)>(), [href]);

        // Act
        book.RemoveSoftHyphens(plan);

        // Assert
        SoftHyphenSurvey after = book.SurveySoftHyphens();
        after.OutsideText.Select(p => p.Name).Should().NotContain("href").And.Contain("id");
        after.Languages.Single(l => l.Key == "pl").SoftHyphens.Should().Be(1, "Polish was not chosen");
    }
}
