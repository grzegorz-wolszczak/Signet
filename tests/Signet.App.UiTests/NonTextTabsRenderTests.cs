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
/// The controls are mounted standalone — <c>DocumentDock</c> hangs the tab rendering under headless.
/// </summary>
public sealed class NonTextTabsRenderTests
{
    private static (Book Book, TabManagerModel Model) LoadMedia(TempDir temp)
    {
        Book book = new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp)).GetBook();
        return (book, new TabManagerModel());
    }

    [AvaloniaFact]
    public void Image_resource_opens_in_an_image_tab_and_renders()
    {
        using TempDir temp = new();
        (Book book, TabManagerModel model) = LoadMedia(temp);
        using (book)
        {
            ImageResource image = book.GetAllResources().OfType<ImageResource>().First();
            OpenTab tab = model.OpenResource(image);
            var vm = new ImageTabViewModel(tab, new StatusBarService(), book);

            vm.InfoLine.Should().Contain("px").And.Contain("KB");
            vm.PixelWidth.Should().BeGreaterThan(0);

            var window = new Window { Width = 400, Height = 300, Content = new ImageTabView { DataContext = vm } };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();
        }
    }

    [AvaloniaFact]
    public void Font_resource_opens_in_a_font_tab_without_throwing()
    {
        using TempDir temp = new();
        (Book book, TabManagerModel model) = LoadMedia(temp);
        using (book)
        {
            FontResource font = book.GetAllResources().OfType<FontResource>().First();
            OpenTab tab = model.OpenResource(font);
            var vm = new FontTabViewModel(tab, book);

            vm.InfoLine.Should().Contain("KB");
            vm.ObfuscationMethod.Should().Be(FontObfuscationMethod.None);

            vm.ObfuscationMethod = FontObfuscationMethod.Idpf;
            font.ObfuscationAlgorithm.Should().NotBeEmpty();
            book.Modified.Should().BeTrue();

            var window = new Window { Width = 400, Height = 300, Content = new FontTabView { DataContext = vm } };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();
        }
    }

    [AvaloniaFact]
    public void Audio_resource_opens_in_a_media_tab_and_renders()
    {
        using TempDir temp = new();
        (Book book, TabManagerModel model) = LoadMedia(temp);
        using (book)
        {
            Resource audio = book.GetAllResources().First(r => r.Type == ResourceType.Audio);
            OpenTab tab = model.OpenResource(audio);
            var vm = new MediaTabViewModel(tab);

            vm.Media.Should().Be(MediaKind.Audio);
            vm.InfoLine.Should().Contain("KB");

            var window = new Window { Width = 400, Height = 300, Content = new MediaTabView { DataContext = vm } };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();
        }
    }
}
