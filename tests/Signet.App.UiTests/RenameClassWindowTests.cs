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

namespace Signet.App.UiTests;

/// <summary>Rendering of the "Rename Class" window.</summary>
public sealed class RenameClassWindowTests
{
    [AvaloniaFact]
    public void Window_shows_validation_in_red_and_enables_rename_only_for_a_valid_name()
    {
        ClassRenamer renamer = new(
            [new ClassRenameSource("ch.xhtml", "<html><body><p class=\"c1\">a</p><p class=\"x\">b</p></body></html>")],
            []);
        RenameClassViewModel vm = new(renamer, new ClassAtCaret("c1", ["html", "body", "p"]));
        RenameClassWindow window = new() { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        Button ok = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "OkButton");
        TextBlock error = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == Strings.Get("RenameClass_SameAsOld"));
        ok.IsEnabled.Should().BeFalse();
        error.IsVisible.Should().BeTrue();
        window.GetVisualDescendants().OfType<RadioButton>().Should().HaveCount(2);

        window.GetVisualDescendants().OfType<TextBox>().Single().Text = "x";
        Dispatcher.UIThread.RunJobs();
        ok.IsEnabled.Should().BeFalse();
        error.Text.Should().Be(Strings.Format("RenameClass_AlreadyExists", "x"));

        window.GetVisualDescendants().OfType<TextBox>().Single().Text = "lead";
        Dispatcher.UIThread.RunJobs();
        ok.IsEnabled.Should().BeTrue();
        error.IsVisible.Should().BeFalse();
        window.Close();
    }

    [AvaloniaFact]
    public void Window_started_from_a_stylesheet_hides_the_scope_choice_and_lists_the_sources()
    {
        ClassRenamer renamer = new(
            [new ClassRenameSource("ch.xhtml", "<html><head><link rel=\"stylesheet\" href=\"s.css\"/></head><body><p class=\"c1\">a</p></body></html>")],
            [new ClassRenameSource("s.css", ".c1 { }\n")]);
        RenameClassWindow window = new() { DataContext = new RenameClassViewModel(renamer, new StyleClassAtCaret("c1", "s.css", -1)) };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        window.GetVisualDescendants().OfType<RadioButton>().Should().OnlyContain(r => !r.IsEffectivelyVisible);
        window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)
            .Should().Contain(Strings.Format("RenameClassWindow_SourceRenamed", "s.css", 1));
        window.Close();
    }
}
