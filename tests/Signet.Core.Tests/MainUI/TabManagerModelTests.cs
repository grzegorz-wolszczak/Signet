using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.MainUI;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.MainUI;

/// <summary>Tests of <see cref="TabManagerModel"/> — opening, activation, closing, navigation, the session.</summary>
public sealed class TabManagerModelTests
{
    private static Book Load(TempDir temp) =>
        new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp)).GetBook();

    private static List<Resource> ThreeDistinctResources(Book book)
    {
        List<Resource> all = book.GetAllResources().ToList();
        Resource html = all.First(r => r.Type == ResourceType.Html);
        Resource css = all.First(r => r.Type == ResourceType.Css);
        Resource image = all.First(r => r.Type == ResourceType.Image);
        return new List<Resource> { html, css, image };
    }

    [Fact]
    public void Opening_three_distinct_resources_creates_three_tabs()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        using TabManagerModel sut = new();
        List<Resource> resources = ThreeDistinctResources(book);

        foreach (Resource resource in resources)
        {
            sut.OpenResource(resource);
        }

        sut.Count.Should().Be(3);
        sut.Tabs.Select(t => t.Resource).Should().Equal(resources);
        sut.ActiveTab!.Resource.Should().Be(resources[2]);
    }

    [Fact]
    public void Reopening_a_resource_activates_the_existing_tab_instead_of_duplicating()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        using TabManagerModel sut = new();
        List<Resource> resources = ThreeDistinctResources(book);
        resources.ForEach(r => sut.OpenResource(r));

        OpenTab first = sut.Tabs[0];
        sut.OpenResource(resources[0]);

        sut.Count.Should().Be(3);
        sut.ActiveTab.Should().BeSameAs(first);
    }

    [Fact]
    public void Closing_the_last_tab_is_ignored_without_force()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        using TabManagerModel sut = new();
        sut.OpenResource(book.GetAllResources().First(r => r.Type == ResourceType.Html));

        sut.CloseTab(0).Should().BeFalse();
        sut.Count.Should().Be(1);

        sut.CloseTab(0, force: true).Should().BeTrue();
        sut.Count.Should().Be(0);
        sut.ActiveTab.Should().BeNull();
    }

    [Fact]
    public void Closing_the_active_tab_activates_the_previously_active_one()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        using TabManagerModel sut = new();
        List<Resource> resources = ThreeDistinctResources(book);
        resources.ForEach(r => sut.OpenResource(r));

        sut.Activate(0);          // history: [2] then [0] active, prev active pushed => history has index-2 tab, current 0
        sut.CloseActiveTab();     // closes tab 0 -> should fall back to the history entry (old active)

        sut.Count.Should().Be(2);
        sut.ActiveTab!.Resource.Should().Be(resources[2]);
    }

    [Fact]
    public void Next_and_previous_tab_wrap_around()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        using TabManagerModel sut = new();
        ThreeDistinctResources(book).ForEach(r => sut.OpenResource(r));

        sut.ActiveIndex.Should().Be(2);
        sut.NextTab();
        sut.ActiveIndex.Should().Be(0);
        sut.PreviousTab();
        sut.ActiveIndex.Should().Be(2);
    }

    [Fact]
    public void Close_other_tabs_keeps_only_the_requested_one()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        using TabManagerModel sut = new();
        List<Resource> resources = ThreeDistinctResources(book);
        resources.ForEach(r => sut.OpenResource(r));

        sut.CloseOtherTabs(1);

        sut.Count.Should().Be(1);
        sut.ActiveTab!.Resource.Should().Be(resources[1]);
    }

    [Fact]
    public void Back_returns_to_the_previously_active_tab()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        using TabManagerModel sut = new();
        List<Resource> resources = ThreeDistinctResources(book);
        resources.ForEach(r => sut.OpenResource(r));

        sut.Activate(0);
        sut.CanGoBack.Should().BeTrue();
        sut.Back();

        sut.ActiveTab!.Resource.Should().Be(resources[2]);
    }

    [Fact]
    public void Deleting_the_underlying_resource_closes_its_tab()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        using TabManagerModel sut = new();
        List<Resource> resources = ThreeDistinctResources(book);
        resources.ForEach(r => sut.OpenResource(r));

        book.GetFolderKeeper().BulkRemoveResources(new[] { resources[1] });

        sut.Count.Should().Be(2);
        sut.Tabs.Select(t => t.Resource).Should().NotContain(resources[1]);
    }

    [Fact]
    public void Renaming_the_underlying_resource_raises_a_caption_change()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        using TabManagerModel sut = new();
        Resource css = book.GetAllResources().First(r => r.Type == ResourceType.Css);
        sut.OpenResource(css);

        OpenTab? renamed = null;
        sut.TabCaptionChanged += (_, e) => renamed = e.Tab;

        Resource[] toRename = { css };
        string[] newNames = { "renamed.css" };
        book.GetFolderKeeper().BulkRenameResources(toRename, newNames);

        renamed.Should().NotBeNull();
        renamed!.Resource.Should().BeSameAs(css);
    }

    [Fact]
    public void Capture_and_restore_session_round_trips_open_tabs_and_the_active_one()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        List<Resource> resources = ThreeDistinctResources(book);

        TabSession session;
        using (TabManagerModel first = new())
        {
            resources.ForEach(r => first.OpenResource(r));
            first.Activate(1);
            session = first.CaptureSession();
        }

        session.OpenBookPaths.Should().HaveCount(3);
        session.ActiveBookPath.Should().Be(resources[1].BookPath);

        using TabManagerModel restored = new();
        restored.RestoreSession(session, book.GetFolderKeeper().GetResourceByBookPathNoThrow);

        restored.Tabs.Select(t => t.Resource.BookPath).Should().Equal(session.OpenBookPaths);
        restored.ActiveTab!.Resource.BookPath.Should().Be(resources[1].BookPath);
    }
}
