using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Signet.App.Resources;
using Signet.Core.BookManipulation;

namespace Signet.App.ViewModels;

/// <summary>
/// Model of the "Rename Id" window opened from the Code Editor context menu: the new name with live
/// validation and the change counter. Unlike "Rename Class" there is no scope choice — an id rename
/// always covers the whole book.
/// </summary>
public sealed partial class RenameIdViewModel : ObservableObject
{
    private readonly IdRenamer _renamer;
    private readonly IdRenameStats _stats;

    /// <summary>Creates the model for renaming <paramref name="oldId"/> with <paramref name="renamer"/>.</summary>
    public RenameIdViewModel(IdRenamer renamer, string oldId)
    {
        _renamer = renamer ?? throw new ArgumentNullException(nameof(renamer));
        OldId = oldId ?? throw new ArgumentNullException(nameof(oldId));
        _newName = oldId;
        _stats = renamer.Preview(oldId);
        UpdateValidation();
    }

    /// <summary>Current id.</summary>
    public string OldId { get; }

    /// <summary>Id label at the top of the window.</summary>
    public string IdLabel => Strings.Format("RenameIdWindow_Id", OldId);

    /// <summary>The entered new name.</summary>
    [ObservableProperty]
    private string _newName;

    /// <summary>Validation messages for the new name (one per line) or an empty string.</summary>
    [ObservableProperty]
    private string _validationError = string.Empty;

    /// <summary>Whether the dialog can be accepted — the new name is valid.</summary>
    [ObservableProperty]
    private bool _canAccept;

    /// <summary>Change counter for the whole-book rename.</summary>
    public string Summary => Strings.Format(
        "RenameIdWindow_Summary", _stats.AttributeOccurrences, _stats.SelectorOccurrences, _stats.ReferencesUpdated, _stats.ChangedFiles);

    /// <summary>Validation messages as a list (for tests).</summary>
    public IReadOnlyList<string> ValidationMessages =>
        ValidationError.Length == 0 ? Array.Empty<string>() : ValidationError.Split('\n');

    /// <summary>Computes the rename for the entered name (it must be valid).</summary>
    public IdRenameResult Rename() => _renamer.Rename(OldId, NewName);

    partial void OnNewNameChanged(string value) => UpdateValidation();

    private void UpdateValidation()
    {
        IdNameValidation validation = _renamer.Validate(OldId, NewName);
        ValidationError = string.Join('\n', validation.Problems.Select(Message));
        CanAccept = validation.IsValid;
    }

    private string Message(IdNameProblem problem) => problem switch
    {
        IdNameProblem.Empty => Strings.Get("RenameId_Empty"),
        IdNameProblem.Invalid => Strings.Get("RenameId_Invalid"),
        IdNameProblem.SameAsOld => Strings.Get("RenameId_SameAsOld"),
        IdNameProblem.AlreadyExists => Strings.Format("RenameId_AlreadyExists", NewName),
        _ => problem.ToString(),
    };
}
