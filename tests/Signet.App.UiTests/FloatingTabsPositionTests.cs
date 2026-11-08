using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Headless.XUnit;
using AwesomeAssertions;
using Signet.App.Docking;
using Signet.App.Services;
using Signet.App.Tabs;
using Signet.App.ViewModels.Tabs;
using Signet.Core;
using Signet.Core.BookManipulation;
using Signet.Core.MainUI;
using Signet.Core.Misc;
using Signet.Core.Resources;
using Signet.Core.Spellcheck;
using Signet.Core.Tests.TestSupport;

namespace Signet.App.UiTests;

/// <summary>The tabs dragged out into windows of their own come back there when the book is opened again.</summary>
public sealed class FloatingTabsPositionTests : IDisposable
{
    private readonly Disposables _disposables = new();

    /// <inheritdoc />
    public void Dispose() => _disposables.Dispose();

    [AvaloniaFact]
    public void A_tab_dragged_out_into_a_window_comes_back_there_with_the_position_of_the_book()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        using Book book = _disposables.Add(new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp)).GetBook());
        string root = _disposables.Add(new TempDir("signet-floattabs-")).Path;
        SettingsStore settings = new(System.IO.Path.Combine(root, "settings.xml"));
        SpellChecker spellChecker = new(settings, System.IO.Path.Combine(root, "hunspell_dictionaries"), System.IO.Path.Combine(root, "user_dictionaries"));
        MainDockFactory dock = new();
        dock.InitLayout(dock.CreateLayout());
        using TabManager tabs = new(dock, new StatusBarService(), settings, spellChecker);
        tabs.SetBook(book);
        List<Resource> all = book.GetAllResources().ToList();
        List<Resource> resources = new() { all.First(r => r.Type == ResourceType.Html), all.First(r => r.Type == ResourceType.Css) };
        tabs.OpenResources(resources);
        ContentTabViewModel css = Documents(dock).Single(d => d.Resource == resources[1]);
        dock.FloatDocumentAt(css, 120, 80, 640, 480).Should().BeTrue();
        BookPosition position = tabs.CapturePosition();
        tabs.CloseAllTabs();

        // Act
        bool restored = tabs.RestorePosition(position);

        // Assert
        restored.Should().BeTrue();
        position.Tabs.Single(t => t.BookPath == resources[1].BookPath).Floating.Should().Be(new FloatingPlace(120, 80, 640, 480));
        position.Tabs.Single(t => t.BookPath == resources[0].BookPath).Floating.Should().BeNull();
        ContentTabViewModel again = Documents(dock).Single(d => d.Resource == resources[1]);
        dock.FloatingBoundsOfDocument(again).Should().NotBeNull();
        ContentTabViewModel stayed = Documents(dock).Single(d => d.Resource == resources[0]);
        dock.FloatingBoundsOfDocument(stayed).Should().BeNull("the other tab was in the main window");
    }

    // Every document tab, in the main window or in a window of its own.
    private static List<ContentTabViewModel> Documents(MainDockFactory dock) =>
        dock.DocumentDock!.VisibleDockables!.OfType<ContentTabViewModel>().ToList()
            .Concat(dock.FloatingDocuments()).ToList();
}
