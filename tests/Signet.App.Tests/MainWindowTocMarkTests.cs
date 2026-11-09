using System;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Signet.App.Tests.TestSupport;
using Signet.App.ViewModels;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// The entries of the Table Of Contents panel that belong to the file of the active tab are marked, and the mark follows the tabs
/// (Calibre 665ef7bdcc). No setting is needed: marking costs one pass over the entries when the tab changes, not per keystroke.
/// </summary>
public sealed class MainWindowTocMarkTests : IDisposable
{
    private readonly Disposables _disposables = new();

    /// <inheritdoc />
    public void Dispose() => _disposables.Dispose();

    [Fact]
    public async Task The_mark_follows_the_active_tab()
    {
        await SingleThreadContext.RunAsync(() =>
        {
            // Arrange
            TempDir temp = _disposables.Add(new TempDir());
            string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Minimal, temp);
            MainWindowViewModel sut = _disposables.Add(new MainWindowViewModel());
            Book book = _disposables.Add(new ImportEpub(epub).GetBook());
            sut.LoadBook(book, epub);
            HtmlResource chapter = book.GetHtmlResources().Single(h => h.Filename == "chapter1.xhtml");
            HtmlResource nav = book.GetNavResource()!;
            TocEntryViewModel entry = sut.TableOfContents.Nodes.Single();

            // Assert
            entry.Entry.TargetBookPath.Should().Be(chapter.BookPath);
            entry.IsCurrent.Should().Be(sut.ActiveTab?.Resource.BookPath == chapter.BookPath, "it follows the tab that opened with the book");

            // Act
            sut.Tabs.OpenResources(new Resource[] { chapter });

            // Assert
            entry.IsCurrent.Should().BeTrue("the entry points at the file of the active tab");

            // Act
            sut.Tabs.OpenResources(new Resource[] { nav });

            // Assert
            entry.IsCurrent.Should().BeFalse("the nav is not what the entry points at");

            // Act
            sut.Tabs.CloseActiveTab();

            // Assert
            entry.IsCurrent.Should().BeTrue("the chapter is active again");
            return Task.CompletedTask;
        });
    }
}
