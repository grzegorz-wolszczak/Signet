using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Resources;
using Signet.App.Services;
using Signet.Core.Misc;
using Signet.Core.Spellcheck;

namespace Signet.App.ViewModels;

/// <summary>One installed (or built-in) spelling dictionary, as the dictionary manager lists it.</summary>
/// <param name="Dictionary">What the installer found on disk.</param>
public sealed record DictionaryRow(InstalledDictionary Dictionary)
{
    /// <summary>The dictionary name — the base name of its files (e.g. <c>pl_PL</c>).</summary>
    public string Name => Dictionary.Name;

    /// <summary>The readable language name (<see cref="SpellChecker.DisplayName"/>).</summary>
    public string DisplayName => SpellChecker.DisplayName(Dictionary.Name);

    /// <summary>How many files on disk belong to the dictionary (none for the built-in one).</summary>
    public int FileCount => Dictionary.Files.Count;

    /// <summary>Whether the dictionary can be used: a broken one says why in <see cref="StateText"/>.</summary>
    public bool Works => Dictionary.Problem is null;

    /// <summary>Whether the dictionary is the built-in one, which has no files and cannot be deleted.</summary>
    public bool IsBuiltIn => Dictionary.IsBuiltIn;

    /// <summary>Built-in, works, overrides the built-in one — or the reason the dictionary does not work.</summary>
    public string StateText => Dictionary.Problem
        ?? Strings.Get(Dictionary.IsBuiltIn
            ? "DictionaryManager_StateBuiltIn"
            : Dictionary.OverridesBuiltIn
                ? "DictionaryManager_StateOverridesBuiltIn"
                : "DictionaryManager_StateWorks");
}

/// <summary>One file of the selected dictionary, with what it is.</summary>
/// <param name="FileName">The file name.</param>
/// <param name="RoleText">What the file is — required, or a companion the spell checker does not read.</param>
public sealed record DictionaryFileRow(string FileName, string RoleText);

/// <summary>
/// View model of the modal "Dictionary manager" window: what is installed in the Hunspell dictionaries folder, the
/// files of the selected dictionary, and the two operations that change the folder — adding a dictionary from files
/// anywhere on disk and deleting one.
/// </summary>
/// <remarks>
/// <para>The work happens at once (it is a file operation, like the user dictionaries on the Spelling page), not on
/// Save: after every change the spell checker rediscovers its dictionaries, and if the primary or the secondary one
/// is gone the setting falls back to the built-in <c>en_US</c> (the primary) or to none (the secondary). The settings
/// this takes are the application's, not the Preferences draft — the spell checker reads those.</para>
/// <para>The window answers the four hooks below (a file picker, two confirmations and a message); left unset — in
/// tests without a UI — the commands that need the user do nothing.</para>
/// </remarks>
public sealed partial class DictionaryManagerViewModel : ObservableObject
{
    private readonly SpellChecker _spellChecker;
    private readonly SettingsStore _settings;

    /// <summary>Creates the view model and reads the dictionaries folder.</summary>
    public DictionaryManagerViewModel(SpellChecker spellChecker, SettingsStore settings)
    {
        _spellChecker = spellChecker ?? throw new ArgumentNullException(nameof(spellChecker));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        // The folder may not exist when the manager is opened for the first time.
        Directory.CreateDirectory(_spellChecker.HunspellDictionariesDirectory);
        Refresh();
    }

    /// <summary>The folder the dictionaries are installed in.</summary>
    public string DictionariesDirectory => _spellChecker.HunspellDictionariesDirectory;

    /// <summary>The header above the list: the folder the dictionaries live in.</summary>
    public string DirectoryText => Strings.Format("DictionaryManager_DirectoryText", DictionariesDirectory);

    /// <summary>The dictionaries available to the spell checker, the built-in one first.</summary>
    public ObservableCollection<DictionaryRow> Dictionaries { get; } = new();

    /// <summary>The files of <see cref="SelectedDictionary"/>, each with what it is.</summary>
    public ObservableCollection<DictionaryFileRow> Files { get; } = new();

    /// <summary>Whether a dictionary was installed or deleted (the Preferences window rebuilds its lists then).</summary>
    public bool Changed { get; private set; }

    /// <summary>
    /// The files to add — a multi-select file picker put up by the window; an empty list = cancelled.
    /// </summary>
    public Func<Task<IReadOnlyList<string>>>? AddFilesRequested { get; set; }

    /// <summary>
    /// Confirms deleting the dictionary (its display name and the files that will be removed).
    /// </summary>
    public Func<string, IReadOnlyList<string>, Task<bool>>? ConfirmDeleteRequested { get; set; }

    /// <summary>
    /// Confirms replacing an already installed dictionary of the same name (a replace, not a merge).
    /// </summary>
    public Func<string, Task<bool>>? ConfirmReplaceRequested { get; set; }

    /// <summary>Shows what went wrong (refused or missing files, a dictionary that cannot be loaded).</summary>
    public Func<string, Task>? ProblemReported { get; set; }

    /// <summary>The dictionary whose files are listed below; the Delete button works on it.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    private DictionaryRow? _selectedDictionary;

    partial void OnSelectedDictionaryChanged(DictionaryRow? value)
    {
        Files.Clear();
        if (value is null)
        {
            return;
        }

        foreach (DictionaryFile file in value.Dictionary.Files)
        {
            Files.Add(new DictionaryFileRow(
                Path.GetFileName(file.Path), Strings.Get("DictionaryManager_Role" + file.Role)));
        }
    }

    /// <summary>Reads the dictionaries folder again, keeping the selected dictionary when it is still there.</summary>
    public void Refresh()
    {
        string? selected = SelectedDictionary?.Name;
        Dictionaries.Clear();
        foreach (InstalledDictionary dictionary in HunspellDictionaryInstaller.List(DictionariesDirectory))
        {
            Dictionaries.Add(new DictionaryRow(dictionary));
        }

        SelectedDictionary = Dictionaries.FirstOrDefault(d => string.Equals(d.Name, selected, StringComparison.OrdinalIgnoreCase))
            ?? Dictionaries.FirstOrDefault();
    }

    /// <summary>
    /// Adds a dictionary from the files the user chooses: refuses what is not a dictionary, asks before replacing an
    /// installed one of the same name, and copies nothing when anything is wrong.
    /// </summary>
    [RelayCommand]
    private async Task AddAsync()
    {
        if (AddFilesRequested is not { } pick)
        {
            return;
        }

        IReadOnlyList<string> files = await pick().ConfigureAwait(true);
        if (files.Count == 0)
        {
            return;
        }

        DictionaryInstallResult result = HunspellDictionaryInstaller.Install(files, DictionariesDirectory, replace: false);
        if (result is { Installed: false, NameTaken: true, Name: { } name })
        {
            if (ConfirmReplaceRequested is not { } confirm || !await confirm(SpellChecker.DisplayName(name)).ConfigureAwait(true))
            {
                return;
            }

            result = HunspellDictionaryInstaller.Install(files, DictionariesDirectory, replace: true);
        }

        if (!result.Installed)
        {
            await ReportAsync(result.Problem).ConfigureAwait(true);
            return;
        }

        ApplyChange();
        SelectedDictionary = Dictionaries.FirstOrDefault(d =>
            string.Equals(d.Name, result.Name, StringComparison.OrdinalIgnoreCase)) ?? SelectedDictionary;
    }

    private bool CanDelete() => SelectedDictionary is { IsBuiltIn: false };

    /// <summary>
    /// Deletes every file of the selected dictionary, after a confirmation that lists them. The built-in dictionary
    /// cannot be deleted.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteAsync()
    {
        if (SelectedDictionary is not { IsBuiltIn: false } row)
        {
            return;
        }

        IReadOnlyList<string> files = HunspellDictionaryInstaller.FilesOf(DictionariesDirectory, row.Name);
        if (ConfirmDeleteRequested is not { } confirm
            || !await confirm(row.DisplayName, files.Select(Path.GetFileName).Select(name => name!).ToList()).ConfigureAwait(true))
        {
            return;
        }

        try
        {
            HunspellDictionaryInstaller.Remove(DictionariesDirectory, row.Name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ReportAsync(ex.Message).ConfigureAwait(true);
        }

        ApplyChange();
    }

    /// <summary>Opens the dictionaries folder in the system file manager.</summary>
    [RelayCommand]
    private async Task OpenDirectoryAsync()
    {
        Directory.CreateDirectory(DictionariesDirectory);
        if (!ExternalOpen.TryOpenFolder(DictionariesDirectory, out string? error))
        {
            await ReportAsync(error).ConfigureAwait(true);
        }
    }

    // After the folder changed: the settings fall back to the built-in dictionary when the one they name is gone, the
    // spell checker rediscovers what is installed, and the list is read again.
    private void ApplyChange()
    {
        IReadOnlyList<string> available = _spellChecker.Dictionaries();
        if (!available.Contains(_settings.Dictionary, StringComparer.Ordinal))
        {
            _settings.Dictionary = SpellChecker.BuiltInDictionaryName;
        }

        if (_settings.SecondaryDictionary.Length > 0
            && !available.Contains(_settings.SecondaryDictionary, StringComparer.Ordinal))
        {
            _settings.SecondaryDictionary = string.Empty;
        }

        _spellChecker.Reload();
        Changed = true;
        Refresh();
    }

    private Task ReportAsync(string? problem) =>
        ProblemReported is { } report && !string.IsNullOrEmpty(problem) ? report(problem) : Task.CompletedTask;
}
