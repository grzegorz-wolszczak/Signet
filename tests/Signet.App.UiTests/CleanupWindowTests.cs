using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Signet.App.Resources;
using Signet.App.ViewModels;
using Signet.App.Views;
using Signet.Core.BookManipulation;
using Signet.Core.Tests.TestSupport;

namespace Signet.App.UiTests;

/// <summary>Rendering of the "Cleanup" window.</summary>
public sealed class CleanupWindowTests
{
    [AvaloniaFact]
    public void Window_shows_the_CSS_HTML_and_Files_tabs_each_with_its_own_Clean_button_and_a_global_Close()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Minimal, temp)).GetBook();
        CleanupViewModel vm = new(CleanupAnalysis.Prepare(book).Analysis!, [], (_, _) => { }, (_, _) => System.Threading.Tasks.Task.FromResult<CleanupAnalysis?>(null));
        CleanupWindow window = new() { DataContext = vm };

        // Act
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        TabControl tabs = window.GetVisualDescendants().OfType<TabControl>().Single();
        tabs.GetVisualDescendants().OfType<TabItem>().Select(t => t.GetVisualDescendants().OfType<TextBlock>().First().Text)

            // Assert
            .Should().Equal(Strings.Get("CleanupWindow_Tab_Css"), Strings.Get("CleanupWindow_Tab_Html"), Strings.Get("CleanupWindow_Tab_Files"));
        window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CloseButton")
            .Content.Should().Be(Strings.Get("Common_Close"));

        for (int i = 0; i < vm.Tabs.Count; i++)
        {
            // Act
            tabs.SelectedIndex = i;
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();

            Button clean = tabs.GetVisualDescendants().OfType<Button>()
                .Single(b => Equals(b.Content, Strings.Get("CleanupWindow_Clean")) && b.IsEffectivelyVisible);

            // Assert
            clean.DataContext.Should().BeSameAs(vm.Tabs[i]);
            clean.IsEffectivelyEnabled.Should().BeFalse();
        }

        window.Close();
    }
}
