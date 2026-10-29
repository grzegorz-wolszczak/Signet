using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Signet.Core.BookManipulation;

namespace Signet.App.Views;

/// <summary>Lists headings that remain at h6 after increasing a parent heading's level.</summary>
public partial class HeadingLevelOverflowWindow : Window
{
    /// <summary>Initializes the window.</summary>
    public HeadingLevelOverflowWindow()
    {
        InitializeComponent();
    }

    /// <summary>Shows the h6 list and returns the selected action.</summary>
    public static Task<HeadingLevelOverflowDecision?> AskAsync(Window owner, IReadOnlyList<Heading> headings) =>
        Build(headings).ShowDialog<HeadingLevelOverflowDecision?>(owner);

    /// <summary>Creates the configured window without showing it.</summary>
    public static HeadingLevelOverflowWindow Build(IReadOnlyList<Heading> headings)
    {
        ArgumentNullException.ThrowIfNull(headings);
        HeadingLevelOverflowWindow window = new();
        window.HeadingList.ItemsSource = headings;
        return window;
    }

    private void OnOpenHeadingClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { DataContext: Heading heading })
        {
            Close(new HeadingLevelOverflowDecision(false, heading));
        }
    }

    private void OnContinueClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        Close(new HeadingLevelOverflowDecision(true, null));

    private void OnCancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        Close(new HeadingLevelOverflowDecision(false, null));
}

/// <summary>The choice made in the h6 overflow dialog.</summary>
public sealed record HeadingLevelOverflowDecision(bool ContinueWithUnchangedHeadings, Heading? HeadingToOpen);
