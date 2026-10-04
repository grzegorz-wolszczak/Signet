using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Signet.App.Resources;
using Signet.App.Views;
using Signet.Core.BookManipulation;

namespace Signet.App.UiTests;

/// <summary>Rendering of the "Remove span" consequences window.</summary>
public sealed class SpanRemovalRiskWindowTests
{
    [AvaloniaFact]
    public void The_window_lists_the_consequences_and_cancels_by_default()
    {
        SpanRemovalPlan plan = new(
            SpanRemovalScope.Book,
            new Dictionary<string, string> { ["a.xhtml"] = "x", ["b.xhtml"] = "y" },
            3,
            new[] { new CleanupConsequence("a.xhtml: <span> — „color: red” (z „.x”) działa na tym spanie", "a.xhtml", 0) });
        Window owner = new();
        owner.Show();

        _ = SpanRemovalRiskWindow.AskAsync(owner, plan);
        Dispatcher.UIThread.RunJobs();
        SpanRemovalRiskWindow window = owner.OwnedWindows.OfType<SpanRemovalRiskWindow>().Single();
        window.CaptureRenderedFrame();

        List<string?> texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        texts.Should().Contain(Strings.Format("SpanRemovalRiskWindow_Intro", 3, 2));
        texts.Should().Contain(t => t != null && t.Contains("color: red"));
        window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CancelButton").IsDefault.Should().BeTrue();
        window.Close();
        owner.Close();
    }
}
