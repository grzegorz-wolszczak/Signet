using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AwesomeAssertions;
using Signet.App.Infrastructure;
using Signet.App.Services;
using Signet.App.ViewModels;
using Signet.App.Views;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;

namespace Signet.App.MainWindowUiTests;

public sealed class BookBrowserDeleteTests
{
    [AvaloniaFact]
    public async Task Deleting_a_new_open_file_closes_its_tab_on_the_UI_thread()
    {
        // Arrange
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Minimal, temp);
        using Book book = new ImportEpub(epub).GetBook();
        using MainWindowViewModel vm = new();
        MainWindow window = new() { DataContext = vm };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();
            vm.ProgressRunner = RunWithoutWindow;
            vm.DelayedProgressRunner = RunWithoutWindow;
            vm.LoadBook(book, epub);
            HtmlResource created = book.CreateEmptyHtmlFile();
            vm.Tabs.OpenResources(new Resource[] { created });
            Dispatcher.UIThread.RunJobs();
            vm.ActiveCodeTab!.Resource.Should().BeSameAs(created);
            int uiThread = Environment.CurrentManagedThreadId;
            List<int> handledOn = new();
            vm.Tabs.ActiveTabChanged += (_, _) => handledOn.Add(Environment.CurrentManagedThreadId);

            // Act
            await vm.BookBrowser.DeleteResourcesAsync(new Resource[] { created });
            Dispatcher.UIThread.RunJobs();

            // Assert
            book.GetAllResources().Should().NotContain(created);
            vm.Tabs.OpenTabViews.Should().NotContain(tab => ReferenceEquals(tab.Resource, created));
            handledOn.Should().NotBeEmpty().And.OnlyContain(id => id == uiThread, "the tab views belong to the UI thread");
        }
        finally
        {
            if (((IBookWorkspace)vm).CurrentBook is not null)
            {
                ((IBookWorkspace)vm).CloseBook();
            }

            window.Close();
        }
    }

    private static ProgressRunner RunWithoutWindow =>
        (_, work, _) => work(new Progress<WorkProgress>(), CancellationToken.None);
}
