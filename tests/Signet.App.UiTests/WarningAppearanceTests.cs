using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Signet.App.Infrastructure;
using Signet.App.Views;
using Signet.Core.BookManipulation;
using Signet.Core.Misc;

namespace Signet.App.UiTests;

/// <summary>
/// The appearance of warning texts in dialogs (Preferences → Appearance → Warnings): by default as large as the
/// interface text, size and color changed live through <see cref="UiDensityManager.ApplyWarning"/>.
/// </summary>
public sealed class WarningAppearanceTests
{
    private const double HeadlessUiFontSize = 14;

    private static (SpanRemovalRiskWindow Window, Window Owner) ShowSpanRemovalRisk()
    {
        SpanRemovalPlan plan = new(
            SpanRemovalScope.File,
            new Dictionary<string, string> { ["a.xhtml"] = "x" },
            1,
            new[] { new CleanupConsequence("a.xhtml: <span> — “color: red” (from “.x”) applies to this span", "a.xhtml", 0) });
        Window owner = new();
        owner.Show();
        _ = SpanRemovalRiskWindow.AskAsync(owner, plan);
        Dispatcher.UIThread.RunJobs();
        SpanRemovalRiskWindow window = owner.OwnedWindows.OfType<SpanRemovalRiskWindow>().Single();
        window.CaptureRenderedFrame();
        return (window, owner);
    }

    private static TextBlock Consequence(Window window) =>
        window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text?.Contains("color: red") == true);

    [AvaloniaFact]
    public void A_warning_is_as_large_as_the_interface_text_by_default()
    {
        Application app = Application.Current!;
        UiDensityManager.ApplyWarning(app, WarningAppearance.Default, HeadlessUiFontSize);
        (SpanRemovalRiskWindow window, Window owner) = ShowSpanRemovalRisk();

        TextBlock intro = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "Intro");
        Consequence(window).FontSize.Should().Be(intro.FontSize);

        window.Close();
        owner.Close();
    }

    [AvaloniaFact]
    public void The_warning_size_and_color_change_live()
    {
        Application app = Application.Current!;
        (SpanRemovalRiskWindow window, Window owner) = ShowSpanRemovalRisk();
        try
        {
            UiDensityManager.ApplyWarning(app, new WarningAppearance(20, "#123456", "#654321"), HeadlessUiFontSize);
            Dispatcher.UIThread.RunJobs();

            TextBlock consequence = Consequence(window);
            consequence.FontSize.Should().Be(20);
            consequence.Foreground.Should().BeOfType<SolidColorBrush>()
                .Which.Color.Should().Be(window.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark
                    ? Color.Parse("#654321")
                    : Color.Parse("#123456"));

            UiDensityManager.ApplyWarning(app, new WarningAppearance(0, "#123456", "#654321"), 17);
            Dispatcher.UIThread.RunJobs();

            consequence.FontSize.Should().Be(17, "0 means the interface text size");
        }
        finally
        {
            UiDensityManager.ApplyWarning(app, WarningAppearance.Default, HeadlessUiFontSize);
            window.Close();
            owner.Close();
        }
    }
}
