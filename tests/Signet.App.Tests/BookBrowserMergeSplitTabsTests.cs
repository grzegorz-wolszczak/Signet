using System;
using System.Linq;
using AutoFixture.Xunit3;
using AwesomeAssertions;
using Signet.App.Services;
using Signet.App.ViewModels;
using Signet.App.ViewModels.Tabs;
using Signet.Core.BookManipulation;
using Signet.Core.MainUI;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// Merge and Split At Markers from the Book Browser rewrite file contents behind the editors — the open tabs must
/// show the new text afterwards (a stale tab would also write the old text back on the next edit).
/// </summary>
public sealed class BookBrowserMergeSplitTabsTests
{
    private sealed class Session : IDisposable
    {
        public Session(TempDir temp)
        {
            string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
            Vm = new MainWindowViewModel();
            Vm.LoadBook(new ImportEpub(epub).GetBook(), epub);
        }

        public MainWindowViewModel Vm { get; }

        public Book Book => ((IBookWorkspace)Vm).CurrentBook!;

        public HtmlResource Chapter => Book.GetAllResources().OfType<HtmlResource>()
            .Single(r => r.BookPath.EndsWith("chapter1.xhtml", StringComparison.Ordinal));

        public CodeTabViewModel OpenChapter()
        {
            Vm.Tabs.CloseAllTabs();
            Vm.Tabs.OpenResources(new Resource[] { Chapter });
            return Vm.ActiveCodeTab!;
        }

        public void Select(params Resource[] resources) =>
            Vm.BookBrowser.UpdateSelection(Vm.BookBrowser.Nodes
                .SelectMany(n => n.Children)
                .Where(n => n.Entry is not null && resources.Contains(n.Entry.Resource))
                .Cast<object?>()
                .ToArray());

        // Puts a split marker followed by a paragraph at the end of the chapter (through the tab) and splits it
        // from the Book Browser; returns the new file.
        public HtmlResource SplitChapter(CodeTabViewModel tab, string secondPart)
        {
            tab.Document.Text = tab.Document.Text.Replace(
                "</body>", $"{CodeViewModel.SectionMarker}<p>{secondPart}</p></body>", StringComparison.Ordinal);
            Select(Chapter);
            Vm.BookBrowser.SplitSelectedCommand.Execute(null);
            return Book.GetAllResources().OfType<HtmlResource>()
                .Single(r => r.BookPath.EndsWith("chapter1_0001.xhtml", StringComparison.Ordinal));
        }

        public void Dispose() => Vm.CheckpointHistory.Dispose();
    }

    [Theory]
    [AutoData]
    public void Split_at_markers_reloads_the_open_tab_of_the_split_file(string secondPart)
    {
        using TempDir temp = new();
        using Session s = new(temp);
        CodeTabViewModel tab = s.OpenChapter();

        s.SplitChapter(tab, secondPart);

        tab.Document.Text.Should().Be(s.Chapter.GetText());
        tab.Document.Text.Should().NotContain(secondPart, "the second part moved to the new file");
        tab.IsModified.Should().BeFalse();
    }

    [Theory]
    [AutoData]
    public void Merge_reloads_the_open_tab_of_the_target_file(string secondPart)
    {
        using TempDir temp = new();
        using Session s = new(temp);
        CodeTabViewModel tab = s.OpenChapter();
        HtmlResource second = s.SplitChapter(tab, secondPart);
        s.Select(s.Chapter, second);

        s.Vm.BookBrowser.MergeSelectedCommand.Execute(null);

        tab.Document.Text.Should().Be(s.Chapter.GetText());
        tab.Document.Text.Should().Contain(secondPart, "the merged file's body is appended to the target");
        tab.IsModified.Should().BeFalse();
    }
}
