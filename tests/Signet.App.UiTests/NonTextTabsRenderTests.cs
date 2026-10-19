using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AwesomeAssertions;
using Signet.App.Services;
using Signet.App.ViewModels.Tabs;
using Signet.App.Views.Tabs;
using Signet.Core;
using Signet.Core.BookManipulation;
using Signet.Core.MainUI;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;

namespace Signet.App.UiTests;

/// <summary>
/// Headless smoke tests of the non-text tabs: every resource type in the corpus
/// (image, font, audio) opens in the proper view model and renders without an exception.
/// The controls are mounted standalone, without the main window.
/// </summary>
public sealed class NonTextTabsRenderTests : IDisposable
{
    private readonly Disposables _disposables = new();

    /// <inheritdoc />
    public void Dispose() => _disposables.Dispose();

    private (Book Book, TabManagerModel Model) LoadMedia(TempDir temp)
    {
        Book book = _disposables.Add(new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp)).GetBook());
        return (book, new TabManagerModel());
    }

    [AvaloniaFact]
    public void Image_resource_opens_in_an_image_tab_and_renders()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        (Book book, TabManagerModel model) = LoadMedia(temp);
        using (book)
        {
            ImageResource image = book.GetAllResources().OfType<ImageResource>().First();
            OpenTab tab = model.OpenResource(image);
            var vm = new ImageTabViewModel(tab, new StatusBarService(), book);

            var window = new Window { Width = 400, Height = 300, Content = new ImageTabView { DataContext = vm } };

            // Act
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();

            // Assert
            vm.InfoLine.Should().Contain("px").And.Contain("KB");
            vm.PixelWidth.Should().BeGreaterThan(0);
        }
    }

    [AvaloniaFact]
    public void Font_resource_opens_in_a_font_tab_without_throwing()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        (Book book, TabManagerModel model) = LoadMedia(temp);
        using (book)
        {
            FontResource font = book.GetAllResources().OfType<FontResource>().First();
            OpenTab tab = model.OpenResource(font);
            var vm = new FontTabViewModel(tab, book);

            vm.InfoLine.Should().Contain("KB");
            vm.ObfuscationMethod.Should().Be(FontObfuscationMethod.None);

            // Act
            vm.ObfuscationMethod = FontObfuscationMethod.Idpf;

            // Assert
            font.ObfuscationAlgorithm.Should().NotBeEmpty();
            book.Modified.Should().BeTrue();

            var window = new Window { Width = 400, Height = 300, Content = new FontTabView { DataContext = vm } };

            // Act
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();
        }
    }

    [AvaloniaFact]
    public void Audio_resource_opens_in_a_media_tab_and_renders()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        (Book book, TabManagerModel model) = LoadMedia(temp);
        using (book)
        {
            Resource audio = book.GetAllResources().First(r => r.Type == ResourceType.Audio);
            OpenTab tab = model.OpenResource(audio);
            var vm = new MediaTabViewModel(tab);

            var window = new Window { Width = 400, Height = 300, Content = new MediaTabView { DataContext = vm } };

            // Act
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();

            // Assert
            vm.Media.Should().Be(MediaKind.Audio);
            vm.InfoLine.Should().Contain("KB");
        }
    }
}
