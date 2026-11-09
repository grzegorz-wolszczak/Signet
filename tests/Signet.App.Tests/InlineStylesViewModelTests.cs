using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Signet.App.ViewModels;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.App.Tests;

/// <summary>The "Extract Inline Styles to Classes" dialog (<see cref="InlineStylesViewModel"/>).</summary>
public sealed class InlineStylesViewModelTests : IDisposable
{
    private readonly Disposables _disposables = new();

    /// <inheritdoc />
    public void Dispose() => _disposables.Dispose();

    // The minimal book whose chapter has `body` instead of its paragraph, and `css` added to its stylesheet.
    private InlineStyleSurvey Survey(string css, string body, out HtmlResource chapter)
    {
        TempDir temp = _disposables.Add(new TempDir());
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Minimal, temp);
        Book book = _disposables.Add(new ImportEpub(epub).GetBook());
        chapter = book.GetHtmlResources().Single(h => h.Filename == "chapter1.xhtml");
        chapter.SetText(chapter.GetText().Replace("<p>Hello, world.</p>", body, StringComparison.Ordinal));
        CssResource sheet = book.GetCssResources().Single();
        sheet.SetText(sheet.GetText() + "\n" + css + "\n");
        return InlineStyleExtraction.Survey(book);
    }

    private const string Body =
        "<p style=\"text-indent:1em\">a</p>\n<p style=\"text-indent:1em\">b</p>\n<p style=\"text-indent:1em\">c</p>\n"
        + "<p style=\"color:red\">d</p>\n<p style=\"color:red\">e</p>\n<p style=\"margin:0\">unique</p>";

    [Fact]
    public void The_classes_are_the_values_that_repeat_numbered_by_use()
    {
        // Arrange
        InlineStyleSurvey survey = Survey(string.Empty, Body, out _);

        // Act
        InlineStylesViewModel sut = new(survey, (_, _) => { }, _ => Task.FromResult(true));

        // Assert
        sut.Classes.Select(c => c.ClassName).Should().Equal("signet-cls-1", "signet-cls-2");
        sut.Classes.Select(c => c.Place).Should().Equal("text-indent: 1em", "color: red");
        sut.Classes.Should().OnlyContain(c => c.IsChecked);
        sut.HasItems.Should().BeTrue();
        sut.CanExtract.Should().BeTrue();
    }

    [Fact]
    public void A_lower_minimum_adds_the_values_used_once_and_never_goes_below_one()
    {
        // Arrange
        InlineStyleSurvey survey = Survey(string.Empty, Body, out _);
        InlineStylesViewModel sut = new(survey, (_, _) => { }, _ => Task.FromResult(true));

        // Act
        sut.MinOccurrences = 1;

        // Assert
        sut.Classes.Should().HaveCount(3);
        sut.Roots.Should().HaveCount(3);

        // Act
        sut.MinOccurrences = 0;

        // Assert
        sut.MinOccurrences.Should().Be(1);

        // Act
        sut.MinOccurrences = 3;

        // Assert
        sut.Classes.Should().ContainSingle().Which.ClassName.Should().Be("signet-cls-1");
        sut.HasItems.Should().BeTrue();

        // Act
        sut.MinOccurrences = 4;

        // Assert
        sut.HasItems.Should().BeFalse();
        sut.CanExtract.Should().BeFalse();
    }

    [Fact]
    public void A_class_that_something_would_override_starts_unchecked_with_the_warning_under_it()
    {
        // Arrange
        InlineStyleSurvey survey = Survey("p.note { text-indent: 0; }", Body.Replace("<p style=\"text-indent", "<p class=\"note\" style=\"text-indent", StringComparison.Ordinal), out _);

        // Act
        InlineStylesViewModel sut = new(survey, (_, _) => { }, _ => Task.FromResult(true));

        // Assert
        InlineStyleClassNode indent = sut.Classes.Single(c => c.Place == "text-indent: 1em");
        indent.IsChecked.Should().BeFalse();
        indent.Children[0].IsWarning.Should().BeTrue();
        indent.Children[0].Title.Should().Contain("p.note");
        sut.Classes.Single(c => c.Place == "color: red").IsChecked.Should().BeTrue();
        sut.Summary.Should().Contain("1").And.Contain("2");
    }

    [Fact]
    public void The_tree_goes_from_the_class_to_the_files_and_the_elements_and_each_row_opens_its_place()
    {
        // Arrange
        InlineStyleSurvey survey = Survey(string.Empty, Body, out HtmlResource chapter);
        List<(string Path, int Offset)> opened = new();
        InlineStylesViewModel sut = new(survey, (path, offset) => opened.Add((path, offset)), _ => Task.FromResult(true));
        InlineStyleClassNode indent = sut.Classes[0];

        // Act
        InlineStyleFileNode file = indent.Children.OfType<InlineStyleFileNode>().Single();
        InlineStyleNode[] elements = file.Children.ToArray();
        indent.Open();
        file.Open();
        elements[2].Open();

        // Assert
        file.Title.Should().Contain(chapter.BookPath).And.Contain("3");
        elements.Should().HaveCount(3);
        elements[0].Title.Should().Contain("<p>");
        elements[0].Place.Should().StartWith("<p style=\"text-indent:1em\">a");
        string text = chapter.GetText();
        opened.Should().Equal(
            (chapter.BookPath, text.IndexOf("<p style", StringComparison.Ordinal)),
            (chapter.BookPath, text.IndexOf("<p style", StringComparison.Ordinal)),
            (chapter.BookPath, text.IndexOf("<p style=\"text-indent:1em\">c", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Extract_applies_the_checked_classes_and_closes_the_dialog()
    {
        // Arrange
        InlineStyleSurvey survey = Survey(string.Empty, Body, out _);
        InlineStyleExtractionPlan? applied = null;
        InlineStylesViewModel sut = new(survey, (_, _) => { }, plan =>
        {
            applied = plan;
            return Task.FromResult(true);
        });
        bool done = false;
        sut.Done += (_, _) => done = true;
        sut.Classes[1].IsChecked = false;

        // Act
        await sut.ExtractCommand.ExecuteAsync(null);

        // Assert
        applied!.StylesheetBookPath.Should().Be(survey.StylesheetBookPath);
        applied.ClassNames.Values.Should().Equal("signet-cls-1");
        done.Should().BeTrue();
    }

    [Fact]
    public async Task Extract_that_did_not_run_leaves_the_dialog_open()
    {
        // Arrange
        InlineStyleSurvey survey = Survey(string.Empty, Body, out _);
        InlineStylesViewModel sut = new(survey, (_, _) => { }, _ => Task.FromResult(false));
        bool done = false;
        sut.Done += (_, _) => done = true;

        // Act
        await sut.ExtractCommand.ExecuteAsync(null);

        // Assert
        done.Should().BeFalse();
    }

    [Fact]
    public void Extract_is_off_without_a_checked_class_and_while_a_book_operation_runs()
    {
        // Arrange
        InlineStyleSurvey survey = Survey(string.Empty, Body, out _);
        InlineStylesViewModel sut = new(survey, (_, _) => { }, _ => Task.FromResult(true));

        // Act
        sut.SelectNoneCommand.Execute(null);

        // Assert
        sut.CanExtract.Should().BeFalse();
        sut.ExtractCommand.CanExecute(null).Should().BeFalse();
        sut.Summary.Should().Contain("0");

        // Act
        sut.SelectAllCommand.Execute(null);

        // Assert
        sut.ExtractCommand.CanExecute(null).Should().BeTrue();

        // Act
        sut.IsBookOperationBusy = true;

        // Assert
        sut.ExtractCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void Nothing_is_announced_when_every_file_links_the_stylesheet()
    {
        // Arrange
        InlineStyleSurvey survey = Survey(string.Empty, Body, out _);

        // Act
        InlineStylesViewModel sut = new(survey, (_, _) => { }, _ => Task.FromResult(true));

        // Assert
        sut.HasOutside.Should().BeFalse();
        sut.StylesheetText.Should().Contain(survey.StylesheetBookPath!);
    }
}
