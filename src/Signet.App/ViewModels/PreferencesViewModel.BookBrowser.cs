using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Signet.Core.MainUI;

namespace Signet.App.ViewModels;

/// <summary>Appearance › Book Browser: which folders are expanded when a book is loaded.</summary>
public sealed partial class PreferencesViewModel
{
    private IReadOnlyList<FolderExpandOption>? _bookBrowserFolders;

    /// <summary>
    /// One checkbox per Book Browser folder (Text, Styles, Images, Fonts, Audio, Video, Misc): whether the folder is
    /// expanded when a book is loaded. Takes effect for the next book loaded, once saved.
    /// </summary>
    public IReadOnlyList<FolderExpandOption> BookBrowserFolders =>
        _bookBrowserFolders ??= Enum.GetValues<OpfModelGroupKind>()
            .Select(kind => new FolderExpandOption(
                kind,
                () => _settings.GetBookBrowserExpandOnLoad(kind),
                value => _settings.SetBookBrowserExpandOnLoad(kind, value)))
            .ToArray();
}

/// <summary>A "expand this folder on load" checkbox of the Book Browser preferences.</summary>
public sealed class FolderExpandOption : ObservableObject
{
    private readonly Func<bool> _get;
    private readonly Action<bool> _set;

    internal FolderExpandOption(OpfModelGroupKind kind, Func<bool> get, Action<bool> set)
    {
        Kind = kind;
        _get = get;
        _set = set;
    }

    /// <summary>The folder.</summary>
    public OpfModelGroupKind Kind { get; }

    /// <summary>The folder name as the Book Browser shows it (Text, Styles, …).</summary>
    public string Label => Kind.ToString();

    /// <summary>Whether the folder is expanded when a book is loaded.</summary>
    public bool IsChecked
    {
        get => _get();
        set
        {
            if (_get() != value)
            {
                _set(value);
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Announces that the value may have changed (e.g. "Restore appearance defaults").</summary>
    public void Refresh() => OnPropertyChanged(nameof(IsChecked));
}
