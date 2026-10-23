using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Signet.App.Services;
using Signet.App.ViewModels;
using Signet.App.Views;
using Signet.Controls.TreeDataGrid;
using Signet.Core.BookManipulation;
using Signet.Core.Misc;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;

namespace Signet.App.UiTests;

/// <summary>Rendering of the font windows: "@font-face rules", "Deleting Fonts" and "Manage Fonts".</summary>
public sealed class FontWindowsTests : IDisposable
{
    private readonly Disposables _disposables = new();

    /// <inheritdoc />
    public void Dispose() => _disposables.Dispose();

    private async Task<(Book Book, BookBrowserViewModel Host, List<FontResource> Fonts)> NewBook(TempDir temp)
    {
        Book book = _disposables.Add(new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Minimal, temp)).GetBook());
        BookBrowserViewModel host = new(
            new SettingsStore(_disposables.Add(new TempDir("signet-fontui-")).Combine("file.xml")), new StatusBarService());
        host.SetBook(book);
        string regular = temp.Combine("Acme-Regular.ttf");
        string bold = temp.Combine("Acme-Bold.ttf");
        File.WriteAllBytes(regular, FontFileBuilder.Ttf("Acme", "Regular", 400, false, fsType: 2));
        File.WriteAllBytes(bold, FontFileBuilder.Ttf("Acme", "Bold", 700, false));
        List<FontResource> fonts = (await host.AddFilesAsync(new[] { regular, bold })).OfType<FontResource>().ToList();
        return (book, host, fonts);
    }

    private static void Render(Window window)
    {
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
    }

    [AvaloniaFact]
    public async Task Font_face_window_shows_a_row_per_font_with_its_warnings_and_the_preview()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        (Book book, BookBrowserViewModel host, List<FontResource> fonts) = await NewBook(temp);
        using (book)
        {
            FontFaceDialogViewModel vm = host.PrepareFontFaces(fonts)!;
            FontFaceWindow window = new() { DataContext = vm };

            // Act
            Render(window);

            // Assert
            window.GetVisualDescendants().OfType<TextBox>().Select(t => t.Text).Should().Contain("Acme").And.Contain("bold");
            window.GetVisualDescendants().OfType<TextBlock>().Should().Contain(t => t.IsEffectivelyVisible && t.Text == vm.Rows[0].WarningText);
            window.GetVisualDescendants().OfType<TextBox>().Single(t => t.IsReadOnly).Text.Should().Contain("@font-face");
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Font_removal_window_lists_the_rules_to_remove()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        (Book book, BookBrowserViewModel host, List<FontResource> fonts) = await NewBook(temp);
        using (book)
        {
            await host.ApplyFontFacesAsync(host.PrepareFontFaces(fonts)!.BuildResult());
            FontRemovalViewModel vm = host.PrepareFontRemoval(fonts.Cast<Resource>().ToList())!;
            FontRemovalWindow window = new() { DataContext = vm };

            // Act
            Render(window);

            // Assert
            window.GetVisualDescendants().OfType<ListBoxItem>().Should().HaveCount(2);
            window.GetVisualDescendants().OfType<CheckBox>().Where(c => c.IsEffectivelyVisible).Should().OnlyContain(c => c.IsChecked == true);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Manage_fonts_window_fills_its_tables()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        (Book book, BookBrowserViewModel host, List<FontResource> fonts) = await NewBook(temp);
        using (book)
        {
            await host.ApplyFontFacesAsync(host.PrepareFontFaces(fonts.Take(1).ToList())!.BuildResult());
            ManageFontsViewModel vm = new(host, null);
            ManageFontsWindow window = ManageFontsWindow.Create(vm, host);

            // Act
            Render(window);

            TreeDataGrid files = window.GetVisualDescendants().OfType<TreeDataGrid>().First(g => g.Name == "FilesGrid");

            // Assert
            files.Rows!.Count.Should().Be(2);
            vm.SelectedFamily!.Family.Should().Be("Acme");
            window.Close();
        }
    }
}
