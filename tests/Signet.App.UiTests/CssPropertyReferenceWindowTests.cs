using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Signet.App.Resources;
using Signet.App.ViewModels;
using Signet.App.Views;
using Signet.Controls.TreeDataGrid;

namespace Signet.App.UiTests;

public sealed class CssPropertyReferenceWindowTests
{
    [AvaloniaFact]
    public void Tables_Show_Display_And_Name_Columns_And_All_Refreshes_Rows()
    {
        // Arrange
        CssPropertyReferenceViewModel viewModel = new();
        CssPropertyReferenceWindow window = new() { DataContext = viewModel };

        // Act
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        CheckBox allCheckBox = window.GetVisualDescendants().OfType<CheckBox>().Single();
        TextBox searchTextBox = window.GetVisualDescendants().OfType<TextBox>().Single();
        Button clearSearchButton = window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "ClearSearchButton");
        TreeDataGrid inheritedGrid = window.GetVisualDescendants().OfType<TreeDataGrid>().Single(grid => grid.Name == "InheritedList");
        TreeDataGrid nonInheritedGrid = window.GetVisualDescendants().OfType<TreeDataGrid>().Single(grid => grid.Name == "NonInheritedList");

        try
        {
            // Assert
            allCheckBox.Content.Should().Be(Strings.Get("CssPropertyReferenceWindow_All"));
            ToolTip.GetTip(allCheckBox).Should().Be(Strings.Get("CssPropertyReferenceWindow_AllTooltip"));
            viewModel.ShowAll.Should().BeFalse();
            searchTextBox.PlaceholderText.Should().Be(Strings.Get("CssPropertyReferenceWindow_SearchPlaceholder"));
            clearSearchButton.IsVisible.Should().BeFalse();
            AssertColumnHeaders(inheritedGrid);
            AssertColumnHeaders(nonInheritedGrid);
            TextBox searchBox = window.GetVisualDescendants().OfType<TextBox>()
                .Single(textBox => textBox.PlaceholderText == Strings.Get("CssPropertyReferenceWindow_SearchPlaceholder"));
            Path searchIcon = searchBox.InnerLeftContent.Should().BeOfType<Path>().Subject;
            searchIcon.Classes.Should().Contain("filterSearchIcon");
            searchIcon.Data.Should().NotBeNull();
            TextBlock displayCell = nonInheritedGrid.GetVisualDescendants().OfType<TextBlock>()
                .Single(textBlock => textBlock.Text == "display");
            string displayTooltipText = viewModel.NonInheritedProperties.Single(row => row.Name == "display").Tooltip;
            ToolTip.SetIsOpen(displayCell, true);
            Dispatcher.UIThread.RunJobs();
            TextBlock displayTooltip = ToolTip.GetTip(displayCell).Should().BeOfType<TextBlock>().Subject;
            displayTooltip.Text.Should().Be(displayTooltipText);
            displayTooltip.TextWrapping.Should().Be(TextWrapping.Wrap);
            ToolTip.SetIsOpen(displayCell, false);

            // Act
            searchTextBox.Text = "f, g";
            Dispatcher.UIThread.RunJobs();

            // Assert
            viewModel.InheritedProperties.Should().Contain(property => property.Name == "font-family");
            viewModel.NonInheritedProperties.Should().Contain(property => property.Name == "margin");
            clearSearchButton.IsVisible.Should().BeTrue();

            // Act
            clearSearchButton.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();

            // Assert
            viewModel.SearchText.Should().BeEmpty();
            clearSearchButton.IsVisible.Should().BeFalse();

            // Act
            allCheckBox.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();

            // Assert
            viewModel.ShowAll.Should().BeTrue();
            viewModel.NonInheritedProperties.Should().ContainSingle(property => property.Name == "grid-template-columns")
                .Subject.Display.Should().Be("g");
            AssertColumnHeaders(nonInheritedGrid);
        }
        finally
        {
            window.Close();
        }
    }

    private static void AssertColumnHeaders(TreeDataGrid grid)
    {
        FlatTreeDataGridSource<CssPropertyReferenceRow> source =
            (FlatTreeDataGridSource<CssPropertyReferenceRow>)grid.Source!;

        source.Columns.Count.Should().Be(2);
        source.Columns[0].Header.Should().Be(Strings.Get("CssPropertyReferenceWindow_Display"));
        source.Columns[1].Header.Should().Be(Strings.Get("CssPropertyReferenceWindow_Name"));
    }
}
