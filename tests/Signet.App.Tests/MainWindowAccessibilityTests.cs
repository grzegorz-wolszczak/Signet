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

/// <summary>Tools → Accessibility in the main window.</summary>
public sealed class MainWindowAccessibilityTests : IDisposable
{
    private readonly Disposables _disposables = new();

    /// <inheritdoc />
    public void Dispose() => _disposables.Dispose();

    private (MainWindowViewModel Sut, Book Book, HtmlResource Chapter) Open()
    {
        TempDir temp = _disposables.Add(new TempDir());
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Minimal, temp);
        MainWindowViewModel sut = _disposables.Add(new MainWindowViewModel());
        Book book = _disposables.Add(new ImportEpub(epub).GetBook());
        HtmlResource chapter = book.GetHtmlResources().Single(h => h.Filename == "chapter1.xhtml");
        chapter.SetText(chapter.GetText().Replace("<p>Hello, world.</p>", "<p>Hello</p><img src=\"a.png\"/>", StringComparison.Ordinal));
        sut.LoadBook(book, epub);
        return (sut, book, chapter);
    }

    [Fact]
    public async Task Without_a_book_there_is_no_dialog()
    {
        await SingleThreadContext.RunAsync(() =>
        {
            // Arrange
            MainWindowViewModel sut = _disposables.Add(new MainWindowViewModel());

            // Act & Assert
            sut.CreateAccessibilityViewModel().Should().BeNull();
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Saving_the_dialog_writes_the_languages_the_descriptions_and_the_metadata_in_one_step()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            using UiCultureScope culture = new("en");
            (MainWindowViewModel sut, Book book, HtmlResource chapter) = Open();
            using AccessibilityViewModel dialog = sut.CreateAccessibilityViewModel()!;
            dialog.NewLanguage = "pl";
            dialog.AddLanguageCommand.Execute(null);
            dialog.MakeMainLanguageCommand.Execute(dialog.Languages.Last());
            dialog.HtmlLanguage = HtmlLanguageChoice.All;
            dialog.Images.Single().Description = "A cat";
            dialog.Summary = "Text with one image.";
            dialog.AccessModes.Single(m => m.Value == "textual").IsChecked = true;

            // Act
            await dialog.SaveCommand.ExecuteAsync(null);

            // Assert
            AccessibilityLanguage.GetLanguages(book).First().Should().Be("pl");
            chapter.GetText().Should().Contain("alt=\"A cat\"").And.Contain("lang=\"pl\"");
            AccessibilityMetadataEditor.Read(book).Summary.Should().Be("Text with one image.");
            book.GetOpf().GetMetaPropertyValues("schema:accessMode").Should().Equal("textual");
            sut.StatusMessage.Should().Be(Strings.Get("Status_AccessibilitySaved"));
        });
    }

    [Fact]
    public async Task The_dialog_is_not_closed_when_nothing_was_changed()
    {
        await SingleThreadContext.RunAsync(() =>
        {
            // Arrange
            (MainWindowViewModel sut, _, _) = Open();
            using AccessibilityViewModel dialog = sut.CreateAccessibilityViewModel()!;

            // Act & Assert
            dialog.CanSave.Should().BeFalse();
            return Task.CompletedTask;
        });
    }
}
