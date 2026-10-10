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

/// <summary>Rendering and accepting of the "Add Footnote" window.</summary>
public sealed class AddFootnoteWindowTests
{
    [AvaloniaFact]
    public void The_window_shows_the_caret_place_and_returns_the_footnote_once_the_text_is_valid()
    {
        const string text = "<html><head><title>t</title></head><body><p>Before after</p></body></html>";
        AddFootnoteViewModel vm = new(
            text, text.IndexOf(" after", System.StringComparison.Ordinal), FootnotePlacement.Global,
            content => FootnoteEditor.ValidateContent(content, "3.0"));
        Window owner = new();
        owner.Show();

        Task<AddFootnoteRequest?> result = AddFootnoteWindow.AskAsync(owner, vm);
        Dispatcher.UIThread.RunJobs();
        AddFootnoteWindow window = owner.OwnedWindows.OfType<AddFootnoteWindow>().Single();
        window.CaptureRenderedFrame();

        TextBlock context = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "ContextText");
        context.Inlines!.OfType<Run>().Select(r => r.Text).Should().Equal("Before", "[*]", " after");
        Button ok = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "OkButton");
        ok.IsEnabled.Should().BeFalse("the text is empty");
        ok.IsDefault.Should().BeFalse("Enter makes a new line in the text");

        vm.Content = "A <i>note</i>.";
        Dispatcher.UIThread.RunJobs();
        ok.IsEnabled.Should().BeTrue();
        ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        result.IsCompleted.Should().BeTrue();
        result.Result.Should().Be(new AddFootnoteRequest("A <i>note</i>.", FootnotePlacement.Global));
        owner.Close();
    }
}
