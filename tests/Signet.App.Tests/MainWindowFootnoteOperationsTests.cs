using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Signet.App.Tests.TestSupport;
using Signet.App.ViewModels;
using Signet.Core.BookManipulation;
using Signet.Core.Footnotes;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// The three heavy footnote operations driven the way the window drives them: the main window's view model prepares
/// the dialog, the test checks the items and runs the dialog's command, and the book is read afterwards. Everything
/// the window itself supplies — the confirmation of a missing checkpoint and the progress window — is a delegate, so
/// no Avalonia window is needed; what is left in the views' code-behind is the wiring of those delegates.
/// </summary>
public sealed class MainWindowFootnoteOperationsTests : IDisposable
{
    private readonly Disposables _disposables = new();

    /// <inheritdoc />
    public void Dispose() => _disposables.Dispose();

    private static string Xhtml(string body) =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE html>\n"
        + "<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\">\n"
        + "<head>\n<title>t</title>\n</head>\n<body>\n" + body + "\n</body>\n</html>";

    // The minimal corpus book with the body of its chapter written anew, opened in the main window's view model.
    private (MainWindowViewModel Sut, Book Book, HtmlResource Chapter) OpenChapterWith(TempDir temp, string body)
    {
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Minimal, temp);
        MainWindowViewModel sut = _disposables.Add(new MainWindowViewModel());
        Book book = _disposables.Add(new ImportEpub(epub).GetBook());
        HtmlResource chapter = book.GetHtmlResources().Single(h => h.Filename == "chapter1.xhtml");
        chapter.SetText(Xhtml(body));
        sut.LoadBook(book, epub);
        sut.Tabs.OpenResources(new Resource[] { chapter });
        return (sut, book, chapter);
    }

    private const string ForeignFootnotes =
        "<p>One<a epub:type=\"noteref\" href=\"#n1\" id=\"r1\">[1]</a> two<a epub:type=\"noteref\" href=\"#n2\" id=\"r2\">[2]</a>.</p>\n"
        + "<section epub:type=\"footnotes\">\n"
        + "<aside epub:type=\"footnote\" id=\"n1\"><p><a href=\"#r1\">1.</a> First.</p></aside>\n"
        + "<aside epub:type=\"footnote\" id=\"n2\"><p><a href=\"#r2\">2.</a> Second.</p></aside>\n"
        + "</section>";

    private static string SignetLocal(string marker, string id, string note) =>
        "<a class=\"signet-fn-ref\" epub:type=\"noteref\" href=\"#fn-1\" id=\"" + id + "\">[" + marker + "]</a>"
        + "\n<section class=\"signet-footnotes\" epub:type=\"footnotes\">\n"
        + "<aside class=\"signet-fn\" epub:type=\"footnote\" id=\"fn-1\"><p class=\"signet-fn-p\">"
        + "<a href=\"#" + id + "\">" + marker + ".</a> " + note + "</p></aside>\n</section>";

    [Fact]
    public async Task Converting_the_footnotes_of_other_origin_from_the_dialog_makes_them_Signets()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            (MainWindowViewModel sut, Book book, HtmlResource chapter) = OpenChapterWith(temp, ForeignFootnotes);

            // Act
            FootnoteConversionViewModel? dialog = await sut.PrepareFootnoteConversionAsync(() => Task.FromResult(true));

            // Assert
            dialog.Should().NotBeNull();
            dialog!.Items.Should().HaveCount(2);
            dialog.HasItems.Should().BeTrue();

            // Act — the user checks every footnote and presses "Convert".
            dialog.SelectAllCommand.Execute(null);

            // Assert
            dialog.CanConvert.Should().BeTrue();

            // Act
            await dialog.ConvertCommand.ExecuteAsync(null);

            // Assert
            dialog.Problems.Should().BeEmpty();
            string text = chapter.GetText();
            text.Should().Contain("class=\"signet-fn-ref\"").And.Contain("class=\"signet-fn\"")
                .And.NotContain("id=\"n1\"").And.NotContain("id=\"n2\"");
            IReadOnlyList<Footnote> footnotes = FootnoteScanner.Scan(book);
            footnotes.Should().HaveCount(2).And.OnlyContain(f => f.IsSignet);
            FootnoteValidator.Validate(book).Should().BeEmpty();

            // Assert — nothing is left to convert, so the dialog has no items any more.
            dialog.Items.Should().BeEmpty();
        });
    }

    [Fact]
    public async Task Changing_the_placement_from_the_dialog_moves_a_local_note_into_the_notes_file()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            (MainWindowViewModel sut, Book book, HtmlResource chapter) =
                OpenChapterWith(temp, "<p>Text" + SignetLocal("1", "fn-1-ref", "Note.") + "</p>");

            // Assert
            FootnoteScanner.Scan(book).Should().ContainSingle()
                .Which.Placement.Should().Be(FootnotePlacement.Local);

            // Act
            FootnotePlacementViewModel? dialog = await sut.PrepareFootnotePlacementAsync();

            // Assert
            dialog.Should().NotBeNull();
            dialog!.HasItems.Should().BeTrue();

            // Act — the user checks the footnote and presses "Apply".
            dialog.SelectAllCommand.Execute(null);

            // Assert
            dialog.CanApply.Should().BeTrue();
            dialog.ToGlobal.Should().Be(1);

            // Act
            await dialog.ApplyCommand.ExecuteAsync(null);

            // Assert
            Footnote moved = FootnoteScanner.Scan(book).Should().ContainSingle().Subject;
            moved.Placement.Should().Be(FootnotePlacement.Global);
            moved.IsSignet.Should().BeTrue();
            moved.NoteFile.Should().NotBeSameAs(chapter, "the note left the chapter for the notes file");
            FootnoteValidator.Validate(book).Should().BeEmpty();
        });
    }

    [Fact]
    public async Task Unifying_the_letters_from_the_dialog_rewrites_them_in_the_chosen_style()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange — a shared note whose letters are roman, while the setting asks for latin.
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            (MainWindowViewModel sut, Book book, HtmlResource chapter) = OpenChapterWith(
                temp,
                "<p>A<a class=\"signet-fn-ref\" epub:type=\"noteref\" href=\"#fn-1\" id=\"fn-1i-ref\">[1i]</a>"
                + " B<a class=\"signet-fn-ref\" epub:type=\"noteref\" href=\"#fn-1\" id=\"fn-1ii-ref\">[1ii]</a>.</p>\n"
                + "<section class=\"signet-footnotes\" epub:type=\"footnotes\">\n"
                + "<aside class=\"signet-fn\" epub:type=\"footnote\" id=\"fn-1\"><p class=\"signet-fn-p\">"
                + "<a href=\"#fn-1i-ref\">1i</a>, <a href=\"#fn-1ii-ref\">1ii</a>. Note.</p></aside>\n</section>");

            // Act
            FootnoteLettersViewModel? dialog = sut.PrepareFootnoteLetters();

            // Assert
            dialog.Should().NotBeNull();
            dialog!.HasItems.Should().BeTrue();
            dialog.Notes.Should().ContainSingle();

            // Act — the user checks the note and presses "Unify".
            dialog.SelectAllCommand.Execute(null);

            // Assert
            dialog.CanUnify.Should().BeTrue();

            // Act
            await dialog.UnifyCommand.ExecuteAsync(null);

            // Assert
            chapter.GetText().Should().Contain("id=\"fn-1a-ref\"").And.Contain("id=\"fn-1b-ref\"")
                .And.NotContain("fn-1i-ref").And.NotContain("fn-1ii-ref");
            FootnoteEditor.PlanLetters(book, FootnoteLetterStyle.LowerLatin).Should().BeEmpty();
            FootnoteValidator.Validate(book).Should().BeEmpty();
        });
    }
}
