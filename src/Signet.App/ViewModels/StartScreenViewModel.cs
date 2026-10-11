using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Signet.App.ViewModels;

/// <summary>A file of the "Recent files" list of the start screen.</summary>
/// <param name="Name">The file name.</param>
/// <param name="Folder">The folder of the file.</param>
/// <param name="Path">The full path.</param>
/// <param name="OpenCommand">Opens the file.</param>
public sealed record StartScreenRecentFile(string Name, string Folder, string Path, ICommand OpenCommand);

/// <summary>
/// The start screen shown in the empty document area while no book is open: "Open…", "New book" and the recent files.
/// </summary>
public sealed partial class StartScreenViewModel : ObservableObject
{
    private readonly Func<IReadOnlyList<string>> _recentFiles;
    private readonly Action<string> _openRecent;

    /// <summary>Creates the start screen.</summary>
    /// <param name="openCommand">"Open…" (File → Open).</param>
    /// <param name="newCommand">"New book" (File → New with the default version).</param>
    /// <param name="recentFiles">The recent files, newest first.</param>
    /// <param name="openRecent">Opens a recent file.</param>
    public StartScreenViewModel(
        ICommand openCommand, ICommand newCommand, Func<IReadOnlyList<string>> recentFiles, Action<string> openRecent)
    {
        OpenCommand = openCommand ?? throw new ArgumentNullException(nameof(openCommand));
        NewCommand = newCommand ?? throw new ArgumentNullException(nameof(newCommand));
        _recentFiles = recentFiles ?? throw new ArgumentNullException(nameof(recentFiles));
        _openRecent = openRecent ?? throw new ArgumentNullException(nameof(openRecent));
        Refresh();
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

    /// <summary>Reads the recent files again (after the list changed).</summary>
    public void Refresh() =>
        RecentFileItems = _recentFiles()
            .Select(path => new StartScreenRecentFile(
                System.IO.Path.GetFileName(path),
                System.IO.Path.GetDirectoryName(path) ?? string.Empty,
                path,
                new RelayCommand(() => _openRecent(path))))
            .ToList();
}
