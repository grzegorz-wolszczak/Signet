using System;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Signet.App.Resources;
using Signet.App.Tests.TestSupport;
using Signet.App.ViewModels;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.App.Tests;

/// <summary>Tools → CSS Tools → Extract Inline Styles… in the main window.</summary>
public sealed class MainWindowInlineStylesTests : IDisposable
{
    private readonly Disposables _disposables = new();

    /// <inheritdoc />
    public void Dispose() => _disposables.Dispose();

    private (MainWindowViewModel Sut, Book Book, HtmlResource Chapter) Open(string body)
    {
        TempDir temp = _disposables.Add(new TempDir());
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Minimal, temp);
        MainWindowViewModel sut = _disposables.Add(new MainWindowViewModel());
        Book book = _disposables.Add(new ImportEpub(epub).GetBook());
        HtmlResource chapter = book.GetHtmlResources().Single(h => h.Filename == "chapter1.xhtml");
        chapter.SetText(chapter.GetText().Replace("<p>Hello, world.</p>", body, StringComparison.Ordinal));
        sut.LoadBook(book, epub);
        return (sut, book, chapter);
    }

    [Fact]
    public async Task A_book_without_style_attributes_gets_a_message_instead_of_the_dialog()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            using UiCultureScope culture = new("en");
            (MainWindowViewModel sut, _, _) = Open("<p>Hello, world.</p>");

            // Act
            InlineStylesViewModel? dialog = await sut.PrepareInlineStylesAsync();

            // Assert
            dialog.Should().BeNull();
            sut.StatusMessage.Should().Be(Strings.Get("Status_NoInlineStyles"));
        });
    }

    [Fact]
    public async Task Extracting_turns_the_repeated_styles_into_classes_of_the_stylesheet()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            using UiCultureScope culture = new("en");
            (MainWindowViewModel sut, Book book, HtmlResource chapter) = Open(
                "<p style=\"text-indent:1em\">a</p><p style=\"text-indent:1em\">b</p><p style=\"margin:0\">c</p>");

            // Act
            InlineStylesViewModel dialog = (await sut.PrepareInlineStylesAsync())!;

            // Assert
            dialog.Classes.Should().ContainSingle();

            // Act
            await dialog.ExtractCommand.ExecuteAsync(null);

            // Assert
            chapter.GetText().Should()
                .Contain("<p class=\"signet-cls-1\">a</p><p class=\"signet-cls-1\">b</p>")
                .And.Contain("<p style=\"margin:0\">c</p>");
            book.GetCssResources().Single().GetText().Should().Contain(".signet-cls-1 {").And.Contain("text-indent: 1em;");
            sut.StatusMessage.Should().Be(Strings.Get("Status_InlineStylesExtracted"));
        });
    }
}
