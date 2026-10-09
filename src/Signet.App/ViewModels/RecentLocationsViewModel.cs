using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Signet.App.Resources;
using Signet.Core.MainUI;
using Signet.Core.Resources;

namespace Signet.App.ViewModels;

/// <summary>
/// The Recent Locations popup (modelled on IntelliJ's): the recently visited places — or, with
/// <see cref="ShowEditedOnly"/>, the recently edited ones — newest first, each with its file, breadcrumb, time and a
/// few lines of text. Typing filters the list, Delete forgets a place, choosing one navigates to it.
/// </summary>
public sealed partial class RecentLocationsViewModel : ViewModelBase
{
    private readonly NavigationHistory _history;
    private readonly Func<string, Resource?> _resolve;
    private readonly Func<string, string?> _textOf;
    private readonly Action<NavigationPlace> _navigate;
    private readonly Func<DateTimeOffset> _clock;
    private readonly int _limit;
    private List<RecentLocationItem> _all = new();

    /// <summary>Creates the popup model.</summary>
    /// <param name="history">The navigation history of the current book.</param>
    /// <param name="resolve">The resource of a bookpath (<c>null</c> when it no longer exists).</param>
    /// <param name="textOf">The current text of a file (of its open tab when there is one).</param>
    /// <param name="navigate">Goes to a place.</param>
    /// <param name="limit">How many places to show.</param>
    /// <param name="toggleShortcutText">The shortcut that toggles <see cref="ShowEditedOnly"/> (empty when none).</param>
    /// <param name="clock">The current time; <see cref="DateTimeOffset.Now"/> by default.</param>
    public RecentLocationsViewModel(
        NavigationHistory history,
        Func<string, Resource?> resolve,
        Func<string, string?> textOf,
        Action<NavigationPlace> navigate,
        int limit,
        string toggleShortcutText,
        Func<DateTimeOffset>? clock = null)
    {
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        _textOf = textOf ?? throw new ArgumentNullException(nameof(textOf));
        _navigate = navigate ?? throw new ArgumentNullException(nameof(navigate));
        _limit = limit;
        _clock = clock ?? (() => DateTimeOffset.Now);
        ToggleShortcutText = toggleShortcutText ?? string.Empty;
        Rebuild();
    }

    /// <summary>Raised when the popup should close (after navigating).</summary>
    public event EventHandler? CloseRequested;

    /// <summary>The shown entries (after <see cref="Filter"/>).</summary>
    public ObservableCollection<RecentLocationItem> Items { get; } = new();

    /// <summary>The selected entry.</summary>
    [ObservableProperty]
    private RecentLocationItem? _selected;

    /// <summary>Whether the list shows the edited places instead of the visited ones.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    private bool _showEditedOnly;

    /// <summary>The filter typed by the user: an entry is shown when its file, breadcrumb or text contains it.</summary>
    [ObservableProperty]
    private string _filter = string.Empty;

    /// <summary>"Recent Locations" / "Recently Edited Locations".</summary>
    public string Title => Strings.Get(ShowEditedOnly ? "RecentLocations_TitleEdited" : "RecentLocations_Title");

    /// <summary>The number of shown entries, e.g. <c>(12)</c>.</summary>
    public string CountText => $"({Items.Count})";

    /// <summary>The shortcut that toggles <see cref="ShowEditedOnly"/> (shown next to the checkbox).</summary>
    public string ToggleShortcutText { get; }

    /// <summary>Goes to the entry's place and closes the popup.</summary>
    public void Navigate(RecentLocationItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        CloseRequested?.Invoke(this, EventArgs.Empty);
        _navigate(item.Place);
    }

    /// <summary>Forgets the selected entry's place (keeping the selection at the same position).</summary>
    public void RemoveSelected()
    {
        if (Selected is not { } item)
        {
            return;
        }

        int index = Items.IndexOf(item);
        _history.Remove(item.Place, ShowEditedOnly);
        Rebuild();
        Selected = Items.Count == 0 ? null : Items[Math.Clamp(index, 0, Items.Count - 1)];
    }

    /// <summary>Moves the selection by <paramref name="delta"/> entries (keyboard Up / Down in the popup).</summary>
    public void MoveSelection(int delta)
    {
        if (Items.Count == 0)
        {
            return;
        }

        int index = Selected is { } item ? Items.IndexOf(item) : -1;
        Selected = Items[Math.Clamp(index + delta, 0, Items.Count - 1)];
    }

    /// <summary>
    /// The time of a place as IntelliJ shows it: "Moments ago" / "N minutes ago" within the last hour, then
    /// "Today 11:52", "Yesterday 11:52" or the full date and time.
    /// </summary>
    public static string FormatTime(DateTimeOffset time, DateTimeOffset now)
    {
        TimeSpan delta = now - time;
        if (delta >= TimeSpan.Zero && delta <= TimeSpan.FromMinutes(61))
        {
            int minutes = (int)Math.Round(delta.TotalMinutes, MidpointRounding.ToEven);
            return minutes switch
            {
                0 => Strings.Get("RecentLocations_MomentsAgo"),
                1 => Strings.Get("RecentLocations_MinuteAgo"),
                >= 60 => Strings.Get("RecentLocations_HourAgo"),
                _ => Strings.Format(IsFewForm(minutes) ? "RecentLocations_MinutesAgoFew" : "RecentLocations_MinutesAgo", minutes),
            };
        }

        DateTime local = time.ToLocalTime().DateTime;
        DateTime today = now.ToLocalTime().Date;
        string clock = local.ToString("t", CultureInfo.CurrentCulture);
        if (local.Date == today)
        {
            return Strings.Format("RecentLocations_Today", clock);
        }

        return local.Date == today.AddDays(-1)
            ? Strings.Format("RecentLocations_Yesterday", clock)
            : local.ToString("g", CultureInfo.CurrentCulture);
    }

    // The Polish "few" plural form (2–4, 22–24…, but not 12–14): "2 minuty", "5 minut". English uses one form.
    private static bool IsFewForm(int n) => n % 10 is >= 2 and <= 4 && n % 100 is < 12 or > 14;

    partial void OnShowEditedOnlyChanged(bool value) => Rebuild();

    partial void OnFilterChanged(string value) => ApplyFilter();

    private void Rebuild()
    {
        DateTimeOffset now = _clock();
        IReadOnlyList<NavigationPlace> places = RecentLocations.Pick(
            ShowEditedOnly ? _history.EditedPlaces : _history.BackPlaces, _limit, p => _resolve(p) is not null);
        _all = places.Select(p => CreateItem(p, now)).OfType<RecentLocationItem>().ToList();
        ApplyFilter();
    }

    private RecentLocationItem? CreateItem(NavigationPlace place, DateTimeOffset now)
    {
        if (_resolve(place.BookPath) is not { } resource || _textOf(place.BookPath) is not { } text)
        {
            return null;
        }

        CodeViewSyntax syntax = CodeViewSyntaxMap.ForResource(resource);
        BreadcrumbKind kind = syntax switch
        {
            CodeViewSyntax.Html or CodeViewSyntax.Xml => BreadcrumbKind.Markup,
            CodeViewSyntax.Css => BreadcrumbKind.Css,
            _ => BreadcrumbKind.None,
        };
        return new RecentLocationItem(
            place,
            resource.Filename,
            RecentLocations.Breadcrumb(text, place.Offset, kind),
            FormatTime(place.Time, now),
            RecentLocations.Snippet(text, place.Offset),
            syntax);
    }

    private void ApplyFilter()
    {
        RecentLocationItem? selected = Selected;
        Items.Clear();
        foreach (RecentLocationItem item in _all.Where(i => i.Matches(Filter)))
        {
            Items.Add(item);
        }

        Selected = selected is not null && Items.Contains(selected) ? selected : Items.FirstOrDefault();
        OnPropertyChanged(nameof(CountText));
    }
}

/// <summary>An entry of the Recent Locations popup.</summary>
/// <param name="Place">The place.</param>
/// <param name="FileName">The file name.</param>
/// <param name="Breadcrumb">The elements / CSS rule around the place (may be empty).</param>
/// <param name="TimeText">When the place was left, as text.</param>
/// <param name="Snippet">The lines around the place.</param>
/// <param name="Syntax">The syntax of the file (for highlighting the snippet).</param>
public sealed record RecentLocationItem(
    NavigationPlace Place, string FileName, string Breadcrumb, string TimeText, LocationSnippet Snippet, CodeViewSyntax Syntax)
{
    /// <summary>Whether the entry contains <paramref name="filter"/> (case-insensitive) in its file, breadcrumb or text.</summary>
    public bool Matches(string filter) =>
        string.IsNullOrEmpty(filter)
        || FileName.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
        || Breadcrumb.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
        || Snippet.Text.Contains(filter, StringComparison.CurrentCultureIgnoreCase);
}
