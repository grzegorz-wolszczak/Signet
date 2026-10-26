using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Signet.App.Infrastructure;
using Signet.App.Services;
using Signet.App.ViewModels;
using Signet.App.Views;
using Signet.Core.BookManipulation;
using Signet.Core.Misc;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;

namespace Signet.App.MainWindowUiTests;

/// <summary>
/// Headless render tests of the main window. They check what the model tests cannot catch:
/// that the window frame actually renders and that the tab area (<c>DocumentDock</c>)
/// gets most of the center column height instead of collapsing to zero in favour of the
/// Validation Results panel.
/// </summary>
/// <remarks>
/// A document tab can be added to <c>DocumentDock</c> and rendered under Avalonia.Headless
/// (checked with Avalonia 12.1.3 / Dock 12.1.0.6 and Dock 12.1.0.9). The tab open/activate/close
/// logic is also covered without UI in <c>Signet.App.Tests</c> (<c>TabManagerTests</c>) and
/// <c>Signet.Core.Tests</c> (<c>TabManagerModelTests</c>).
/// </remarks>
public sealed class WindowRenderTests : IDisposable
{
    private readonly Disposables _disposables = new();

    /// <inheritdoc />
    public void Dispose() => _disposables.Dispose();

    private MainWindow ShowMainWindow()
    {
        MainWindow window = new() { DataContext = _disposables.Add(new MainWindowViewModel()) };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaFact]
    public void Main_window_renders_menu_toolbars_and_dock()
    {
        // Act
        MainWindow window = ShowMainWindow();

        // Assert
        window.GetVisualDescendants().OfType<Avalonia.Controls.Menu>().Should().NotBeEmpty();
        window.GetVisualDescendants().Any(v => v.GetType().Name.Contains("DockControl"))
            .Should().BeTrue();
    }

    [AvaloniaFact]
    public void The_zoom_in_the_status_bar_has_the_font_size_of_the_rest_of_the_status_bar()
    {
        // Act
        MainWindow window = ShowMainWindow();

        // The buttons of the status bar: the ones in the same panel as the text of the open book.
        TextBlock bookFile = window.FindControl<TextBlock>("BookFileText")!;
        Panel? statusPanel = bookFile.GetVisualAncestors().OfType<StackPanel>().First();
        Button[] buttons = statusPanel.GetVisualDescendants().OfType<Button>().ToArray();
        Button percent = buttons.Single(b => Equals(b.Content, "100%"));
        Button zoomOut = buttons.Single(b => Equals(b.Content, "−"));
        double statusText = bookFile.FontSize;

        // Assert
        percent.FontSize.Should().Be(statusText);
        zoomOut.FontSize.Should().Be(statusText);
    }

    [AvaloniaFact]
    public void Toolbar_buttons_render_colour_icons_from_the_default_icon_theme()
    {
        // Arrange
        string settingsPath = _disposables.Add(new TempDir("uitests-")).Combine("file.xml");

        // Act
        new IconThemeManager(new SettingsStore(settingsPath), NullLogger<IconThemeManager>.Instance).ApplySaved();

        MainWindow window = ShowMainWindow();

        // Toolbar button icons (not the TreeView arrow PathIcons in the Book Browser).
        List<Image> icons = window.GetVisualDescendants().OfType<Image>()
            .Where(i => i.DataContext is ToolbarItemViewModel { IconKey: not null })
            .ToList();

        // Assert
        icons.Should().NotBeEmpty("the New/Open/Save toolbar should show icons");
        static bool IsColourPng(Image i) =>
            IconThemeManager.ColoredIconKeys.Contains(((ToolbarItemViewModel)i.DataContext!).IconKey!);
        icons.Where(i => !IsColourPng(i)).Should().OnlyContain(i => i.Source is DrawingImage,
            "the default \"main\" set consists of colour DrawingImages resolved by IconKeyToImageConverter");

        // Beautify / Fix code on the Tools toolbar use colour PNGs (beautify-html / beautify-css / html-fix).
        // (Cleanup's "polish" icon is a menu item only, not on a toolbar by default.)
        icons.Where(IsColourPng).Select(i => ((ToolbarItemViewModel)i.DataContext!).IconKey)
            .Should().Contain("beautify-html").And.Contain("beautify-css").And.Contain("html-fix")
            .And.OnlyContain(k => IconThemeManager.ColoredIconKeys.Contains(k!));
        icons.Where(IsColourPng).Should().OnlyContain(i => i.Source is Bitmap);
    }

    [AvaloniaFact]
    public void Documents_area_gets_the_bulk_of_the_center_column_height()
    {
        // Act
        MainWindow window = ShowMainWindow();

        Control? documentControl = window.GetVisualDescendants()
            .OfType<Control>()
            .FirstOrDefault(v => v.GetType().Name is "DocumentControl" or "DocumentDockControl");

        // Assert
        documentControl.Should().NotBeNull(
            "the tab area must render even with no open documents");

        double windowHeight = window.Bounds.Height;
        documentControl!.Bounds.Height.Should().BeGreaterThan(windowHeight * 0.4,
            "an empty DocumentDock must not collapse to zero in favour of the Validation Results panel");
    }

    [AvaloniaFact]
    public void Opening_a_resource_renders_a_code_editor_tab_in_the_document_dock()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        Book book = _disposables.Add(new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp)).GetBook());

        // Act
        MainWindow window = ShowMainWindow();
        MainWindowViewModel vm = (MainWindowViewModel)window.DataContext!;
        vm.LoadBook(book, string.Empty);
        Dispatcher.UIThread.RunJobs();

        string bookPath = book.GetAllResources().OfType<HtmlResource>().First().BookPath;
        vm.NavigateToBookPathAtOffset(bookPath, 0);
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        // Assert
        vm.ActiveCodeTab.Should().NotBeNull();
        window.GetVisualDescendants().OfType<AvaloniaEdit.TextEditor>().Should().NotBeEmpty(
            "the tab added to the DocumentDock must be materialized and rendered");

        // Closing the book detaches the view model from it (a view model that stays reachable gets later language-change notifications).
        // Act
        ((IBookWorkspace)vm).CloseBook();
        window.Close();
    }
}
