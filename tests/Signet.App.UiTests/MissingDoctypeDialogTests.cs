using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Signet.App.Resources;
using Signet.App.Services;
using Signet.App.Views;

namespace Signet.App.UiTests;

/// <summary>
/// Headless tests of <see cref="MissingDoctypeDialog"/>: the list of files, the fixed window size
/// with a long list, and "Don't ask again" disabling the No button.
/// </summary>
public sealed class MissingDoctypeDialogTests
{
    private static readonly string[] OneFile = { "a.xhtml" };

    private static void ShowAndRender(Window window)
    {
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
    }

    private static Button ButtonWith(Window window, string content) =>
        window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == content);

    [AvaloniaFact]
    public void Lists_the_files_under_a_header_with_the_operation_name()
    {
        string[] files = { "OEBPS/Text/ch1.xhtml", "OEBPS/Text/ch2.xhtml" };
        MissingDoctypeDialog window = MissingDoctypeDialog.Build("Prettify", files);
        ShowAndRender(window);

        window.Title.Should().Be(Strings.Get("MissingDoctypeDialog_Title"));
        window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)
            .Should().Contain(Strings.Format("MissingDoctypeDialog_Header", "Prettify", 2));
        window.GetVisualDescendants().OfType<ListBox>().Single().ItemsSource.Should().BeEquivalentTo(files);
    }

    [AvaloniaFact]
    public void A_long_list_scrolls_instead_of_growing_the_window()
    {
        string[] files = Enumerable.Range(1, 300).Select(i => $"OEBPS/Text/chapter{i}.xhtml").ToArray();
        MissingDoctypeDialog window = MissingDoctypeDialog.Build("Prettify", files);
        double height = window.Height;
        ShowAndRender(window);

        window.Bounds.Height.Should().Be(height);
        ScrollViewer scroller = window.GetVisualDescendants().OfType<ScrollViewer>().First();
        scroller.Extent.Height.Should().BeGreaterThan(scroller.Viewport.Height);
    }

    [AvaloniaFact]
    public void Dont_ask_again_disables_No_and_turns_Yes_into_remembered_continue()
    {
        MissingDoctypeDialog window = MissingDoctypeDialog.Build("Prettify", OneFile);
        ShowAndRender(window);
        CheckBox dontAsk = window.GetVisualDescendants().OfType<CheckBox>().Single();
        Button no = ButtonWith(window, Strings.Get("Common_No"));

        no.IsEnabled.Should().BeTrue();
        window.Answer.Should().Be(MissingDoctypeAnswer.Continue);

        dontAsk.IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        no.IsEnabled.Should().BeFalse();
        window.Answer.Should().Be(MissingDoctypeAnswer.ContinueAndDontAskAgain);

        dontAsk.IsChecked = false;
        Dispatcher.UIThread.RunJobs();

        no.IsEnabled.Should().BeTrue();
    }
}
