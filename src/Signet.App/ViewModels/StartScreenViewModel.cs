using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Resources;
using Signet.App.Services;
using Signet.Core.Misc;

namespace Signet.App.ViewModels;

/// <summary>A file of the "Recent files" list of the start screen.</summary>
/// <param name="Name">The file name.</param>
/// <param name="Folder">The folder of the file.</param>
/// <param name="Path">The full path.</param>
/// <param name="OpenCommand">Opens the file.</param>
/// <param name="RemoveCommand">Removes the file from the list of recent files (the context menu of the entry).</param>
/// <param name="OpenFolderCommand">
/// "Open File Location" (the context menu of the entry): shows the file selected in the system file manager.
/// </param>
/// <param name="IsHighlighted">Whether the entry is drawn as if the pointer were on it (the sample in Preferences only).</param>
/// <param name="IsMissing">
/// Whether the file is no longer on disk: the entry is drawn dimmed and its tooltip says why it cannot be opened, but it
/// stays clickable — the click offers to take it off the list. "Open File Location" does not apply to it.
/// </param>
/// <param name="ToolTip">The tooltip of the entry: the full path, and the reason when <paramref name="IsMissing"/>.</param>
public sealed record StartScreenRecentFile(
    string Name, string Folder, string Path, ICommand OpenCommand, ICommand RemoveCommand, ICommand OpenFolderCommand,
    bool IsHighlighted = false, bool IsMissing = false, string ToolTip = "");

/// <summary>
/// The start screen shown in the empty document area while no book is open: "Open…", "New book" and the recent files.
/// </summary>
public sealed partial class StartScreenViewModel : ObservableObject, IDisposable
{
    // The fabricated books of the sample start screen (the preview in Preferences): a name and a folder each, the first
    // longer than the others to show how a long name is cut.
    private static readonly (string Name, string Folder)[] SampleFiles =
    {
        ("The Lighthouse Keeper.epub", @"D:\Books\Fiction"),
        ("A Short History of Everything (2003) - Bill Bryson.epub", @"D:\Calibre Library\Bryson"),
        ("Field Notes.epub", @"D:\Books\Drafts"),
        ("The Long Way Home - Collected Stories.epub", @"D:\Books\Fiction\Stories"),
        ("Cooking for Two.epub", @"D:\Books\Kitchen"),
        ("Letters from the North.epub", @"D:\Calibre Library\Archive"),
        ("Atlas of Forgotten Places.epub", @"D:\Books\Travel"),
        ("The Clockmaker's Daughter.epub", @"D:\Books\Fiction"),
        ("Notes on Typography.epub", @"D:\Books\Design"),
        ("Winter Garden.epub", @"D:\Books\Fiction"),
        ("Quiet Machines.epub", @"D:\Books\Science"),
        ("Sketchbook.epub", @"D:\Books\Drafts"),
    };

    // The entry the sample draws as if the pointer were on it, so its background shows without moving the mouse.
    private const int SampleHighlightedIndex = 1;

    private readonly Func<IReadOnlyList<string>> _recentFiles;
    private readonly Action<string> _openRecent;
    private readonly Action<string>? _removeRecent;
    private readonly SettingsStore? _settings;

    /// <summary>Creates the start screen.</summary>
    /// <param name="openCommand">"Open…" (File → Open).</param>
    /// <param name="newCommand">"New book" (File → New with the default version).</param>
    /// <param name="recentFiles">The recent files to show, newest first.</param>
    /// <param name="openRecent">Opens a recent file.</param>
    /// <param name="removeRecent">
    /// Removes a file from the list of recent files (the caller refreshes the screen, e.g. through the change event of its list);
    /// <c>null</c> = the entries cannot be removed.
    /// </param>
    /// <param name="settings">
    /// The settings the look of the list comes from (<see cref="Appearance"/>); the screen follows their changes. <c>null</c> = the
    /// default look.
    /// </param>
    public StartScreenViewModel(
        ICommand openCommand, ICommand newCommand, Func<IReadOnlyList<string>> recentFiles, Action<string> openRecent,
        Action<string>? removeRecent = null, SettingsStore? settings = null)
    {
        OpenCommand = openCommand ?? throw new ArgumentNullException(nameof(openCommand));
        NewCommand = newCommand ?? throw new ArgumentNullException(nameof(newCommand));
        _recentFiles = recentFiles ?? throw new ArgumentNullException(nameof(recentFiles));
        _openRecent = openRecent ?? throw new ArgumentNullException(nameof(openRecent));
        _removeRecent = removeRecent;
        _settings = settings;
        if (_settings is not null)
        {
            _settings.SettingChanged += OnSettingChanged;
        }

        Refresh();
    }

    /// <summary>
    /// Creates the sample start screen of the preview in Preferences: the real view model with fabricated books (the history of
    /// the user is not read) that do nothing, one of them drawn as if the pointer were on it. It follows <paramref name="settings"/>
    /// (the draft of the window), so the number of the files and the look change in it at once.
    /// </summary>
    public static StartScreenViewModel CreateSample(SettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        RelayCommand nothing = new(() => { });
        StartScreenViewModel sample = new(
            nothing, nothing,
            () => SampleFiles.Take(settings.StartScreenRecentFilesLimit).Select(f => System.IO.Path.Combine(f.Folder, f.Name)).ToList(),
            _ => { }, null, settings)
        {
            IsSample = true,
            IsVisible = true,
        };
        sample.Refresh();
        return sample;
    }

    /// <summary>Whether this is the sample start screen of the Preferences window.</summary>
    public bool IsSample { get; private init; }

    /// <summary>The look of the list of recent files now (the settings); the default look without settings.</summary>
    public StartScreenAppearance Appearance => _settings?.StartScreenAppearance ?? StartScreenAppearance.Default;

    /// <summary>Raised when a setting of the look of the list changed (the view applies <see cref="Appearance"/> again).</summary>
    public event EventHandler? AppearanceChanged;

    private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        if (e.QualifiedKey.Contains("start_screen_recent_files_limit", StringComparison.Ordinal))
        {
            Refresh();
        }
        else if (e.QualifiedKey.Contains("start_screen_", StringComparison.Ordinal))
        {
            AppearanceChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_settings is not null)
        {
            _settings.SettingChanged -= OnSettingChanged;
        }
    }

    /// <summary>"Open…".</summary>
    public ICommand OpenCommand { get; }

    /// <summary>"New book".</summary>
    public ICommand NewCommand { get; }

    /// <summary>Whether the screen is shown (no book is open).</summary>
    [ObservableProperty]
    private bool _isVisible;

    /// <summary>The recent files, newest first.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRecentFiles))]
    private IReadOnlyList<StartScreenRecentFile> _recentFileItems = Array.Empty<StartScreenRecentFile>();

    /// <summary>Whether there are recent files to show.</summary>
    public bool HasRecentFiles => RecentFileItems.Count > 0;

    /// <summary>
    /// Reads the recent files again (after the list changed, and every time the screen is shown): the existence of every
    /// path is checked here, so a file removed from disk meanwhile shows as missing. The fabricated books of the sample
    /// are never on disk and are never drawn as missing.
    /// </summary>
    public void Refresh() =>
        RecentFileItems = _recentFiles()
            .Select((path, index) =>
            {
                bool missing = !IsSample && !System.IO.File.Exists(path);
                return new StartScreenRecentFile(
                    System.IO.Path.GetFileName(path),
                    System.IO.Path.GetDirectoryName(path) ?? string.Empty,
                    path,
                    new RelayCommand(() => _openRecent(path)),
                    new RelayCommand(() => _removeRecent?.Invoke(path), () => _removeRecent is not null),
                    new RelayCommand(() => ExternalOpen.TryShowInFolder(path, out _), () => !missing && !IsSample),
                    IsSample && index == SampleHighlightedIndex,
                    missing,
                    missing ? path + Environment.NewLine + Strings.Get("RecentFiles_FileMissingTooltip") : path);
            })
            .ToList();

    // The screen comes back every time the book is closed: the list is read again, so a file that has gone meanwhile is
    // dimmed without waiting for the list itself to change.
    partial void OnIsVisibleChanged(bool value)
    {
        if (value && !IsSample)
        {
            Refresh();
        }
    }
}
