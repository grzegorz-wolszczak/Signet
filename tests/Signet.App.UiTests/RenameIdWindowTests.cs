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

/// <summary>Rendering of the "Rename Id" window.</summary>
public sealed class RenameIdWindowTests
{
    [AvaloniaFact]
    public void Window_shows_validation_in_red_and_enables_rename_only_for_a_valid_name()
    {
        // Arrange
        IdRenamer renamer = new(
            [new ClassRenameSource("ch.xhtml", "<html><body><p id=\"note1\">a</p><p id=\"other\">b</p></body></html>")],
            []);
        RenameIdViewModel vm = new(renamer, "note1");

        // Act
        RenameIdWindow window = new() { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        Button ok = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "OkButton");
        TextBlock error = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == Strings.Get("RenameId_SameAsOld"));

        // Assert
        ok.IsEnabled.Should().BeFalse();
        error.IsVisible.Should().BeTrue();

        // Act
        window.GetVisualDescendants().OfType<TextBox>().Single().Text = "other";
        Dispatcher.UIThread.RunJobs();

        // Assert
        ok.IsEnabled.Should().BeFalse();
        error.Text.Should().Be(Strings.Format("RenameId_AlreadyExists", "other"));

        // Act
        window.GetVisualDescendants().OfType<TextBox>().Single().Text = "note2";
        Dispatcher.UIThread.RunJobs();

        // Assert
        ok.IsEnabled.Should().BeTrue();
        error.IsVisible.Should().BeFalse();
        window.Close();
    }

    [AvaloniaFact]
    public void Accept_closes_with_the_result_and_cancel_closes_with_null()
    {
        // Arrange
        IdRenamer renamer = new([new ClassRenameSource("ch.xhtml", "<html><body><p id=\"note1\">a</p></body></html>")], []);
        Window owner = new();
        owner.Show();

        // Act
        var acceptTask = RenameIdWindow.AskAsync(owner, new RenameIdViewModel(renamer, "note1"));
        Dispatcher.UIThread.RunJobs();
        RenameIdWindow acceptWindow = owner.OwnedWindows.OfType<RenameIdWindow>().Single();
        acceptWindow.GetVisualDescendants().OfType<TextBox>().Single().Text = "note2";
        Dispatcher.UIThread.RunJobs();
        acceptWindow.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "OkButton")
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        // Assert
        acceptTask.IsCompletedSuccessfully.Should().BeTrue();
        acceptTask.Result!.ChangedTexts["ch.xhtml"].Should().Contain("id=\"note2\"");

        // Act
        var cancelTask = RenameIdWindow.AskAsync(owner, new RenameIdViewModel(renamer, "note1"));
        Dispatcher.UIThread.RunJobs();
        RenameIdWindow cancelWindow = owner.OwnedWindows.OfType<RenameIdWindow>().Single();
        cancelWindow.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CancelButton")
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        // Assert
        cancelTask.IsCompletedSuccessfully.Should().BeTrue();
        cancelTask.Result.Should().BeNull();
        owner.Close();
    }
}
