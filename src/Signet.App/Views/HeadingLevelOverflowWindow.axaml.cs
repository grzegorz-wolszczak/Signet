using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Signet.App.Resources;
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
        window.HeadingList.ItemsSource = headings.Select(HeadingOverflowEntry.For).ToList();
        return window;
    }

    private void OnShowHeadingClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { DataContext: HeadingOverflowEntry entry })
        {
            Close(new HeadingLevelOverflowDecision(false, entry.Heading));
        }
    }

    private void OnContinueClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        Close(new HeadingLevelOverflowDecision(true, null));

    private void OnCancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        Close(new HeadingLevelOverflowDecision(false, null));
}

/// <summary>A heading in the h6 list: its title and where it is ("Text/ch1.xhtml, line 12").</summary>
/// <param name="Heading">The heading.</param>
/// <param name="Title">The text shown for the heading.</param>
/// <param name="Location">The file and the line of the heading's opening tag.</param>
public sealed record HeadingOverflowEntry(Heading Heading, string Title, string Location)
{
    /// <summary>The entry of <paramref name="heading"/>.</summary>
    public static HeadingOverflowEntry For(Heading heading)
    {
        ArgumentNullException.ThrowIfNull(heading);
        string path = heading.ResourceFile.BookPath;
        string location = heading.SourceOffset < 0
            ? path
            : Strings.Format("HeadingSelectorWindow_HeadingLocation", path, LineOf(heading.ResourceFile.GetText(), heading.SourceOffset));
        return new HeadingOverflowEntry(heading, heading.Text, location);
    }

    private static int LineOf(string text, int offset) =>
        text.AsSpan(0, Math.Min(offset, text.Length)).Count('\n') + 1;
}

/// <summary>The choice made in the h6 overflow dialog.</summary>
/// <param name="ContinueWithUnchangedHeadings">Whether the level change goes on, leaving the listed headings at h6.</param>
/// <param name="HeadingToShow">A heading to select in the heading selector instead (the level is not changed).</param>
public sealed record HeadingLevelOverflowDecision(bool ContinueWithUnchangedHeadings, Heading? HeadingToShow);
