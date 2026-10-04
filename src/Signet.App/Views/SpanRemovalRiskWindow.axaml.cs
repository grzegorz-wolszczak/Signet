using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Signet.App.Resources;
using Signet.Core.BookManipulation;

namespace Signet.App.Views;

/// <summary>
/// "Remove span" — the consequences for the book's styling (<see cref="SpanRemovalPlan.Consequences"/>), shown before
/// the removal so the user accepts them deliberately: "Remove anyway" carries it out, Cancel (the default) does not.
/// </summary>
public partial class SpanRemovalRiskWindow : Window
{
    /// <summary>Initializes the window.</summary>
    public SpanRemovalRiskWindow()
    {
        InitializeComponent();
        AcceptButton.Click += (_, _) => Close(true);
        CancelButton.Click += (_, _) => Close(false);
    }

    /// <summary>Shows the consequences of <paramref name="plan"/>; <c>true</c> when the user accepted them.</summary>
    public static async Task<bool> AskAsync(Window owner, SpanRemovalPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        SpanRemovalRiskWindow window = new();
        window.Intro.Text = Strings.Format("SpanRemovalRiskWindow_Intro", plan.SpanCount, plan.NewTexts.Count);
        window.Consequences.ItemsSource = plan.Consequences;
        return await window.ShowDialog<bool>(owner);
    }
}
