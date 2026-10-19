using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Signet.App.Docking;
using Signet.App.Services;
using Signet.App.Tabs;
using Signet.App.Tests.TestSupport;
using Signet.App.ViewModels.Tabs;
using Signet.Core;
using Signet.Core.BookManipulation;
using Signet.Core.Misc;
using Signet.Core.Resources;
using Signet.Core.Spellcheck;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// Tests for the App layer of the tab manager: mapping <c>TabManagerModel</c> onto the Dock.Avalonia
/// <c>DocumentDock</c> area (opening, activating an existing tab, closing).
/// </summary>
public sealed class TabManagerTests : IDisposable
{
    private readonly Disposables _disposables = new();

    /// <inheritdoc />
    public void Dispose() => _disposables.Dispose();

    private (TabManager Tabs, MainDockFactory Dock) NewManager()
    {
        StatusBarService statusBar = new();
        MainDockFactory dockFactory = new();
        dockFactory.InitLayout(dockFactory.CreateLayout());
        (SettingsStore settings, SpellChecker spellChecker) = _disposables.Add(SpellCheckTestFactory.New());
        TabManager tabs = new(dockFactory, statusBar, settings, spellChecker);
        return (tabs, dockFactory);
    }

    private Book Load(TempDir temp) =>
        _disposables.Add(new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp)).GetBook());

    private static List<Resource> ThreeResources(Book book)
    {
        List<Resource> all = book.GetAllResources().ToList();
        return new List<Resource>
        {
            all.First(r => r.Type == ResourceType.Html),
            all.First(r => r.Type == ResourceType.Css),
            all.First(r => r.Type == ResourceType.Image),
        };
    }

    private static List<ContentTabViewModel> Documents(MainDockFactory dock) =>
        dock.DocumentDock!.VisibleDockables!.OfType<ContentTabViewModel>().ToList();

    [Fact]
    public void Opening_three_resources_puts_three_documents_in_the_dock()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        using Book book = Load(temp);
        (TabManager tabs, MainDockFactory dock) = NewManager();
        tabs.SetBook(book);

        List<Resource> resources = ThreeResources(book);

        // Act
        tabs.OpenResources(resources);

        // Assert
        Documents(dock).Select(d => d.Resource).Should().Equal(resources);
        dock.DocumentDock!.ActiveDockable.Should().BeSameAs(Documents(dock)[2]);
        tabs.Dispose();
    }

    /// <summary>
    /// A closed image tab lets go of its image: the resource lives as long as the book, and a tab still subscribed to it
    /// stayed in memory with its bitmap and reloaded it whenever the file changed on disk.
    /// </summary>
    [Fact]
    public void A_closed_image_tab_no_longer_follows_its_file_on_disk()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        using Book book = Load(temp);
        (TabManager tabs, MainDockFactory dock) = NewManager();
        tabs.SetBook(book);
        ImageResource image = book.GetAllResources().OfType<ImageResource>().First();

        // Act
        tabs.OpenResource(image);
        ImageTabViewModel view = Documents(dock).OfType<ImageTabViewModel>().Single();
        List<string?> changes = new();
        view.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        image.NotifyUpdatedOnDisk();

        // Assert
        changes.Should().Contain(nameof(ImageTabViewModel.InfoLine), "an open tab follows its file");

        // Act
        tabs.CloseAllTabs();
        changes.Clear();
        image.NotifyUpdatedOnDisk();

        // Assert
        changes.Should().BeEmpty("a closed tab no longer follows the file");
        tabs.Dispose();
    }

    [Fact]
    public void Reopening_a_resource_activates_the_existing_document()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        using Book book = Load(temp);
        (TabManager tabs, MainDockFactory dock) = NewManager();
        tabs.SetBook(book);
        List<Resource> resources = ThreeResources(book);
        tabs.OpenResources(resources);

        // Act
        tabs.OpenResource(resources[0]);

        // Assert
        Documents(dock).Should().HaveCount(3);
        dock.DocumentDock!.ActiveDockable.Should().BeSameAs(Documents(dock)[0]);
        tabs.Dispose();
    }

    [Fact]
    public void Closing_a_document_from_the_dock_updates_the_model()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        using Book book = Load(temp);
        (TabManager tabs, MainDockFactory dock) = NewManager();
        tabs.SetBook(book);
        List<Resource> resources = ThreeResources(book);
        tabs.OpenResources(resources);

        // Act
        dock.CloseDockable(Documents(dock)[1]);

        // Assert
        tabs.Model.Count.Should().Be(2);
        Documents(dock).Select(d => d.Resource).Should().NotContain(resources[1]);
        tabs.Dispose();
    }

    [Fact]
    public void Unsupported_resources_do_not_open_a_tab()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        using Book book = Load(temp);
        (TabManager tabs, MainDockFactory dock) = NewManager();
        tabs.SetBook(book);

        Resource? generic = book.GetAllResources().FirstOrDefault(r => r.Type == ResourceType.Generic);
        if (generic is not null)
        {
            // Act
            tabs.OpenResource(generic);

            // Assert
            Documents(dock).Should().BeEmpty();
        }

        tabs.Dispose();
    }

    [Fact]
    public void Session_capture_and_restore_reopens_the_same_tabs()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        using Book book = Load(temp);
        TempDir folder = _disposables.Add(new TempDir("signet-tabs-"));
        SettingsStore settings = new(folder.Combine("settings.xml"));
        StatusBarService statusBar = new();
        List<Resource> resources = ThreeResources(book);
        SpellChecker spellChecker = new(
            settings,
            folder.Combine("dicts"),
            folder.Combine("userdicts"));

        MainDockFactory dock1 = new();
        dock1.InitLayout(dock1.CreateLayout());
        using (TabManager first = new(dock1, statusBar, settings, spellChecker))
        {
            // Act
            first.SetBook(book);
            first.OpenResources(resources);
            first.CaptureSession(settings);
        }

        // Arrange
        MainDockFactory dock2 = new();
        dock2.InitLayout(dock2.CreateLayout());
        using TabManager restored = new(dock2, statusBar, settings, spellChecker);

        // Act
        restored.SetBook(book);
        restored.RestoreSession(settings);

        // Assert
        Documents(dock2).Select(d => d.Resource.BookPath)
            .Should().Equal(resources.Select(r => r.BookPath));
    }

    [Fact]
    public void Session_capture_and_restore_reopens_the_cursor_offset_of_each_code_tab()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        using Book book = Load(temp);
        TempDir folder = _disposables.Add(new TempDir("signet-tabs-"));
        SettingsStore settings = new(folder.Combine("settings.xml"));
        StatusBarService statusBar = new();
        Resource html = book.GetAllResources().First(r => r.Type == ResourceType.Html);
        SpellChecker spellChecker = new(
            settings,
            folder.Combine("dicts"),
            folder.Combine("userdicts"));

        MainDockFactory dock1 = new();
        dock1.InitLayout(dock1.CreateLayout());
        using (TabManager first = new(dock1, statusBar, settings, spellChecker))
        {
            first.SetBook(book);

            // Act
            first.OpenResource(html);
            var code = (CodeTabViewModel)Documents(dock1).Single(d => d.Resource.BookPath == html.BookPath);
            code.UpdateCaret(line: 1, column: 1, offset: 5, codepoint: -1);
            first.CaptureSession(settings);
        }

        // Arrange
        MainDockFactory dock2 = new();
        dock2.InitLayout(dock2.CreateLayout());
        using TabManager restored = new(dock2, statusBar, settings, spellChecker);
        restored.SetBook(book);

        // Open the tab up front (as a real CodeEditor would) to capture the caret scroll request
        // raised by RestoreSession. GoToOffset by itself does not set CaretOffset (the editor view
        // does that only after receiving the event).
        restored.OpenResource(html);
        var restoredCode = (CodeTabViewModel)Documents(dock2).Single(d => d.Resource.BookPath == html.BookPath);
        int? requestedOffset = null;
        restoredCode.ScrollToOffsetRequested += offset => requestedOffset = offset;

        // Act
        restored.RestoreSession(settings);

        // Assert
        requestedOffset.Should().Be(5);
    }

    [Fact]
    public void Zooming_one_code_tab_applies_the_same_zoom_to_other_open_code_tabs_and_settings()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        using Book book = Load(temp);
        (SettingsStore settings, SpellChecker spellChecker) = _disposables.Add(SpellCheckTestFactory.New());
        MainDockFactory dock = new();
        dock.InitLayout(dock.CreateLayout());
        using TabManager tabs = new(dock, new StatusBarService(), settings, spellChecker);
        tabs.SetBook(book);
        List<Resource> all = book.GetAllResources().ToList();
        tabs.OpenResources(new[] { all.First(r => r.Type == ResourceType.Html), all.First(r => r.Type == ResourceType.Css) });
        List<CodeTabViewModel> codeTabs = Documents(dock).OfType<CodeTabViewModel>().ToList();

        // Act
        codeTabs[0].ZoomFactor = 1.5;

        // Assert
        codeTabs[1].ZoomFactor.Should().Be(1.5);
        settings.ZoomText.Should().Be(1.5f);
    }

    [Fact]
    public void Newly_opened_code_tab_starts_with_the_shared_code_zoom()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        using Book book = Load(temp);
        (SettingsStore settings, SpellChecker spellChecker) = _disposables.Add(SpellCheckTestFactory.New());
        MainDockFactory dock = new();
        dock.InitLayout(dock.CreateLayout());
        using TabManager tabs = new(dock, new StatusBarService(), settings, spellChecker);
        tabs.SetBook(book);
        List<Resource> all = book.GetAllResources().ToList();
        tabs.OpenResource(all.First(r => r.Type == ResourceType.Html));

        // Act
        Documents(dock).OfType<CodeTabViewModel>().Single().ZoomFactor = 2.0;

        tabs.OpenResource(all.First(r => r.Type == ResourceType.Css));

        // Assert
        Documents(dock).OfType<CodeTabViewModel>().Should().OnlyContain(t => t.ZoomFactor == 2.0);
    }
}
