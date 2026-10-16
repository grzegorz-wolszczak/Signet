using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Signet.App.ViewModels;
using Signet.App.Views;
using Signet.Core.Footnotes;

namespace Signet.App.UiTests;

/// <summary>Rendering and accepting of the footnote window (adding and editing a footnote).</summary>
public sealed class FootnoteWindowTests
{
    [AvaloniaFact]
    public void The_window_shows_the_caret_place_and_returns_the_footnote_once_the_text_is_valid()
    {
        const string text = "<html><head><title>t</title></head><body><p>Before after</p></body></html>";
        FootnoteViewModel vm = new(
            text, text.IndexOf(" after", System.StringComparison.Ordinal), FootnotePlacement.Global, 2, 7,
            content => FootnoteEditor.ValidateContent(content, "3.0"));
        Window owner = new();
        owner.Show();

        Task<FootnoteRequest?> result = FootnoteWindow.AskAsync(owner, vm);
        Dispatcher.UIThread.RunJobs();
        FootnoteWindow window = owner.OwnedWindows.OfType<FootnoteWindow>().Single();
        window.CaptureRenderedFrame();

        TextBlock context = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "ContextText");
        context.Inlines!.OfType<Run>().Select(r => r.Text).Should().Equal("Before", "[7]", " after");
        vm.IsLocal = true;
        Dispatcher.UIThread.RunJobs();
        context.Inlines!.OfType<Run>().Select(r => r.Text).Should().Equal("Before", "[2]", " after");
        vm.IsGlobal = true;
        Button ok = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "OkButton");
        ok.IsEnabled.Should().BeFalse("the text is empty");
        ok.IsDefault.Should().BeFalse("Enter makes a new line in the text");
        window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "RemoveButton").IsVisible
            .Should().BeFalse("a new footnote has nothing to remove");

        vm.Content = "A <i>note</i>.";
        Dispatcher.UIThread.RunJobs();
        ok.IsEnabled.Should().BeTrue();
        ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        result.IsCompleted.Should().BeTrue();
        result.Result.Should().Be(new FootnoteRequest("A <i>note</i>.", FootnotePlacement.Global));
        owner.Close();
    }

    private static FootnoteViewModel Edit()
    {
        const string text = "<html><head><title>t</title></head><body><p>Before<a href=\"#fn-1\">[1]</a> after</p></body></html>";
        int start = text.IndexOf("<a", System.StringComparison.Ordinal);
        int end = text.IndexOf("</a>", System.StringComparison.Ordinal) + 4;
        return FootnoteViewModel.ForEdit(
            text, start, end, new FootnoteContent("Old text.", false), FootnotePlacement.Local, 1, 4,
            content => FootnoteEditor.ValidateContent(content, "3.0"));
    }

    [AvaloniaFact]
    public void An_edited_footnote_is_saved_once_changed()
    {
        FootnoteViewModel vm = Edit();
        Window owner = new();
        owner.Show();

        Task<FootnoteRequest?> result = FootnoteWindow.AskAsync(owner, vm);
        Dispatcher.UIThread.RunJobs();
        FootnoteWindow window = owner.OwnedWindows.OfType<FootnoteWindow>().Single();
        window.CaptureRenderedFrame();

        window.Title.Should().Be(vm.Title);
        TextBlock context = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "ContextText");
        context.Inlines!.OfType<Run>().Select(r => r.Text).Should().Equal("Before", "[1]", " after");
        Button remove = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "RemoveButton");
        remove.IsVisible.Should().BeTrue();
        remove.Classes.Should().Contain("danger");
        Button ok = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "OkButton");
        ok.Content.Should().Be(vm.AcceptText);
        ok.Classes.Should().Contain(ButtonClasses.Accent, "Save is blue");
        ok.IsEnabled.Should().BeFalse("nothing changed yet");

        vm.Content = "New text.";
        Dispatcher.UIThread.RunJobs();
        ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        result.IsCompleted.Should().BeTrue();
        result.Result.Should().Be(new FootnoteRequest("New text.", FootnotePlacement.Local));
        owner.Close();
    }

    [AvaloniaFact]
    public void A_footnote_that_cannot_change_its_placement_keeps_it_and_says_why()
    {
        FootnoteViewModel vm = Edit();
        vm.PlacementBlocked = FootnoteViewModel.ErrorSentence(FootnoteError.SeveralFiles);
        Window owner = new();
        owner.Show();

        _ = FootnoteWindow.AskAsync(owner, vm);
        Dispatcher.UIThread.RunJobs();
        FootnoteWindow window = owner.OwnedWindows.OfType<FootnoteWindow>().Single();
        window.CaptureRenderedFrame();

        StackPanel placement = window.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "PlacementPanel");
        placement.IsEnabled.Should().BeFalse();
        ToolTip.GetTip(placement).Should().Be(vm.PlacementBlocked);
        vm.CanChangePlacement.Should().BeFalse();
        owner.Close();
    }

    [AvaloniaFact]
    public void Remove_asks_first_and_can_stop_asking()
    {
        bool stopped = false;
        FootnoteViewModel vm = Edit();
        vm.StopConfirmingRemoval = () => stopped = true;
        Window owner = new();
        owner.Show();

        Task<FootnoteRequest?> result = FootnoteWindow.AskAsync(owner, vm);
        Dispatcher.UIThread.RunJobs();
        FootnoteWindow window = owner.OwnedWindows.OfType<FootnoteWindow>().Single();
        Button remove = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "RemoveButton");

        // Cancelled: the footnote window stays open.
        remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        ConfirmWindow confirm = window.OwnedWindows.OfType<ConfirmWindow>().Single();
        confirm.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CancelButton")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        result.IsCompleted.Should().BeFalse();

        // Confirmed with "Don't ask again".
        remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        confirm = window.OwnedWindows.OfType<ConfirmWindow>().Single();
        confirm.CaptureRenderedFrame();
        CheckBox dontAsk = confirm.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "DontAskAgainBox");
        dontAsk.IsVisible.Should().BeTrue();
        confirm.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "OkButton").Classes
            .Should().Contain(ButtonClasses.Danger, "the confirming Remove is red");
        dontAsk.IsChecked = true;
        confirm.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "OkButton")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        stopped.Should().BeTrue();
        result.IsCompleted.Should().BeTrue();
        result.Result.Should().Be(new FootnoteRequest("Old text.", FootnotePlacement.Local, Remove: true));
        owner.Close();
    }

    [AvaloniaFact]
    public void Remove_without_the_confirmation_closes_right_away()
    {
        FootnoteViewModel vm = Edit();
        vm.ConfirmRemoval = false;
        Window owner = new();
        owner.Show();

        Task<FootnoteRequest?> result = FootnoteWindow.AskAsync(owner, vm);
        Dispatcher.UIThread.RunJobs();
        FootnoteWindow window = owner.OwnedWindows.OfType<FootnoteWindow>().Single();
        window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "RemoveButton")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        result.IsCompleted.Should().BeTrue();
        result.Result!.Remove.Should().BeTrue();
        owner.Close();
    }

    [AvaloniaFact]
    public void The_code_preview_shows_the_markup_of_a_valid_text()
    {
        const string text = "<html><head><title>t</title></head><body><p>Before after</p></body></html>";
        FootnoteViewModel vm = new(
            text, text.IndexOf(" after", System.StringComparison.Ordinal), FootnotePlacement.Local, 1, 1,
            content => FootnoteEditor.ValidateContent(content, "3.0"))
        {
            Preview = (content, _, number) => new FootnoteMarkup("<a>[" + number + "]</a>", "<aside>" + content + "</aside>", "page"),
        };
        Window owner = new();
        owner.Show();

        Task<FootnoteRequest?> result = FootnoteWindow.AskAsync(owner, vm);
        Dispatcher.UIThread.RunJobs();
        FootnoteWindow window = owner.OwnedWindows.OfType<FootnoteWindow>().Single();
        window.CaptureRenderedFrame();
        AvaloniaEdit.TextEditor code = window.GetVisualDescendants().OfType<AvaloniaEdit.TextEditor>().Single(e => e.Name == "PreviewCodeEditor");
        code.IsVisible.Should().BeFalse("the text is empty");

        vm.Content = "One.\n\nTwo.";
        Dispatcher.UIThread.RunJobs();

        code.IsVisible.Should().BeTrue();
        code.Text.Should().Be("<a>[1]</a>\n\n<aside>One.\n\nTwo.</aside>");
        window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "ContentHintText").Text
            .Should().Be(vm.ContentHint);
        window.Close();
        Dispatcher.UIThread.RunJobs();
        result.IsCompleted.Should().BeTrue();
        owner.Close();
    }
}
