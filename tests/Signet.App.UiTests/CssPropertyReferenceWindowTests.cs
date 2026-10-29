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

namespace Signet.App.UiTests;

public sealed class CssPropertyReferenceWindowTests
{
    [AvaloniaFact]
    public void All_CheckBox_Refreshes_ReadOnly_Property_Lists()
    {
        // Arrange
        CssPropertyReferenceViewModel viewModel = new();
        CssPropertyReferenceWindow window = new() { DataContext = viewModel };

        // Act
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        CheckBox allCheckBox = window.GetVisualDescendants().OfType<CheckBox>().Single();

        // Assert
        allCheckBox.Content.Should().Be(Strings.Get("CssPropertyReferenceWindow_All"));
        viewModel.ShowAll.Should().BeFalse();
        window.GetVisualDescendants().OfType<TextBox>().Should().BeEmpty();

        // Act
        allCheckBox.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        // Assert
        viewModel.ShowAll.Should().BeTrue();
        window.GetVisualDescendants().OfType<TextBlock>()
            .Should().Contain(text => text.Text == "grid-template-columns");
        window.Close();
    }
}
