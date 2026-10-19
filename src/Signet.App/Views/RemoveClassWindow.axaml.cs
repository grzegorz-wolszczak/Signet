using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Signet.App.Resources;
using Signet.Core.BookManipulation;

namespace Signet.App.Views;

/// <summary>
/// "Remove Class" — where and how many times the class is used, shown before the removal so the user confirms it
/// deliberately: "Remove" carries it out, Cancel (the default) does not.
/// </summary>
public partial class RemoveClassWindow : Window
{
    /// <summary>Initializes the window.</summary>
    public RemoveClassWindow()
    {
        InitializeComponent();
        AcceptButton.Click += (_, _) => Close(true);
        CancelButton.Click += (_, _) => Close(false);
    }

    /// <summary>
    /// Shows where <paramref name="className"/> is used (<paramref name="summary"/>, one entry per file); <c>true</c>
    /// when the user confirmed the removal.
    /// </summary>
    public static async Task<bool> AskAsync(
        Window owner, string className, int totalOccurrences, IReadOnlyList<ClassUsageFileSummary> summary)
    {
        ArgumentNullException.ThrowIfNull(className);
        ArgumentNullException.ThrowIfNull(summary);

        RemoveClassWindow window = new();
        window.Intro.Text = Strings.Format("RemoveClassWindow_Intro", className, totalOccurrences, summary.Count);
        window.Usages.ItemsSource = summary;
        return await window.ShowDialog<bool>(owner);
    }
}
