using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using AwesomeAssertions;
using Signet.App.Infrastructure;
using Signet.Core.Misc;

namespace Signet.App.UiTests;

/// <summary>The weight of the interface font and of the document tabs, set as application resources the styles read.</summary>
public sealed class FontWeightTests
{
    [AvaloniaFact]
    public void The_interface_weight_reaches_the_text_of_a_window_and_goes_away_when_not_set()
    {
        // Arrange
        Application app = Application.Current!;
        TextBlock text = new() { Text = "Save" };
        Window window = new() { Content = text };
        try
        {
            // Act
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // Assert
            text.FontWeight.Should().Be(FontWeight.Normal);

            // Act
            UiDensityManager.ApplyUiFontWeight(app, 300);
            Dispatcher.UIThread.RunJobs();

            // Assert
            text.FontWeight.Should().Be(FontWeight.Light, "the text inherits the weight of its window");

            // Act
            UiDensityManager.ApplyUiFontWeight(app, 0);
            Dispatcher.UIThread.RunJobs();

            // Assert
            text.FontWeight.Should().Be(FontWeight.Normal);
        }
        finally
        {
            UiDensityManager.ApplyUiFontWeight(app, 0);
            window.Close();
        }
    }

    [AvaloniaFact]
    public void The_weight_of_the_document_tabs_is_a_resource_that_is_removed_when_not_set()
    {
        // Arrange
        Application app = Application.Current!;
        try
        {
            // Act
            UiDensityManager.ApplyDocumentTab(app, new DocumentTabFontAppearance(string.Empty, 0, 600));

            // Act & Assert
            app.Resources.TryGetResource(UiDensityManager.DocumentTabFontWeightKey, null, out object? weight).Should().BeTrue();

            // Assert
            weight.Should().Be(FontWeight.SemiBold);

            // Act
            UiDensityManager.ApplyDocumentTab(app, DocumentTabFontAppearance.Default);

            // Act & Assert
            app.Resources.TryGetResource(UiDensityManager.DocumentTabFontWeightKey, null, out _).Should().BeFalse();
        }
        finally
        {
            UiDensityManager.ApplyDocumentTab(app, DocumentTabFontAppearance.Default);
        }
    }

    [AvaloniaFact]
    public void The_weights_of_a_font_are_read_from_the_font_manager_and_are_at_least_regular()
    {
        // Act
        System.Collections.Generic.IReadOnlyList<int> weights = FontCatalog.WeightsOf(string.Empty);

        // Assert
        weights.Should().NotBeEmpty().And.BeInAscendingOrder();
        weights.Should().Contain(400);

        // Act & Assert
        FontCatalog.WeightsOf("No Such Font, Arial").Should().Equal(FontCatalog.WeightsOf("No Such Font"), "the first name of a list counts");
    }
}
