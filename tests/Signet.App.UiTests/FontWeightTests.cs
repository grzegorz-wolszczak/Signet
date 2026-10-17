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
        Application app = Application.Current!;
        TextBlock text = new() { Text = "Save" };
        Window window = new() { Content = text };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            text.FontWeight.Should().Be(FontWeight.Normal);

            UiDensityManager.ApplyUiFontWeight(app, 300);
            Dispatcher.UIThread.RunJobs();

            text.FontWeight.Should().Be(FontWeight.Light, "the text inherits the weight of its window");

            UiDensityManager.ApplyUiFontWeight(app, 0);
            Dispatcher.UIThread.RunJobs();

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
        Application app = Application.Current!;
        try
        {
            UiDensityManager.ApplyDocumentTab(app, new DocumentTabFontAppearance(string.Empty, 0, 600));
            app.Resources.TryGetResource(UiDensityManager.DocumentTabFontWeightKey, null, out object? weight).Should().BeTrue();
            weight.Should().Be(FontWeight.SemiBold);

            UiDensityManager.ApplyDocumentTab(app, DocumentTabFontAppearance.Default);

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
        System.Collections.Generic.IReadOnlyList<int> weights = FontCatalog.WeightsOf(string.Empty);

        weights.Should().NotBeEmpty().And.BeInAscendingOrder();
        weights.Should().Contain(400);
        FontCatalog.WeightsOf("No Such Font, Arial").Should().Equal(FontCatalog.WeightsOf("No Such Font"), "the first name of a list counts");
    }
}
