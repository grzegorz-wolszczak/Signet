using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using Signet.Core.BookManipulation;
using Signet.Core.Localization;

namespace Signet.App.ViewModels;

/// <summary>
/// View model of the "Validation Results" panel — the results of Check Book (Well-Formed Check EPUB). The main window
/// performs the navigation when an entry is activated, applies the automatic fixes and keeps the skipped rules.
/// </summary>
public sealed partial class ValidationResultsViewModel : ViewModelBase
{
    private bool _hasResults;
    private bool _hasFixable;
    private bool _canFixSelection;
    private IReadOnlyList<ValidationResultRow> _selection = Array.Empty<ValidationResultRow>();

    /// <summary>Raised when an entry is activated (double click) — handled by the main window.</summary>
    public event EventHandler<ValidationResult>? EntryActivated;

    /// <summary>Raised by "Fix" / "Fix all" with the results to fix (all of them have a fix).</summary>
    public event EventHandler<IReadOnlyList<ValidationResult>>? FixRequested;

    /// <summary>Raised by "Skip this type of problem" with the rule code.</summary>
    public event EventHandler<string>? SkipRuleRequested;

    /// <summary>Raised by "Restore" with the rule codes to check again.</summary>
    public event EventHandler<IReadOnlyList<string>>? RestoreRulesRequested;

    /// <summary>Rows of the current validation results.</summary>
    public ObservableCollection<ValidationResultRow> Rows { get; } = new();

    /// <summary>The rules whose problems are left out, with their names.</summary>
    public ObservableCollection<SkippedRule> SkippedRules { get; } = new();

    /// <summary>
    /// Whether there are any results (to switch the view between the table and the "No problems found!" message).
    /// </summary>
    public bool HasResults
    {
        get => _hasResults;
        private set => SetProperty(ref _hasResults, value);
    }

    /// <summary>Whether any result has an automatic fix ("Fix all").</summary>
    public bool HasFixable
    {
        get => _hasFixable;
        private set => SetProperty(ref _hasFixable, value);
    }

    /// <summary>Whether a selected result has an automatic fix ("Fix").</summary>
    public bool CanFixSelection
    {
        get => _canFixSelection;
        private set => SetProperty(ref _canFixSelection, value);
    }

    /// <summary>Whether some rules are skipped (the "Restore" button).</summary>
    public bool HasSkippedRules => SkippedRules.Count > 0;

    /// <summary>Replaces the current results with new ones (clears and fills again).</summary>
    public void LoadResults(IReadOnlyList<ValidationResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        Rows.Clear();
        foreach (ValidationResult result in results)
        {
            Rows.Add(new ValidationResultRow(result));
        }

        HasResults = Rows.Count > 0;
        HasFixable = Rows.Any(r => r.CanFix);
        SetSelection(Array.Empty<ValidationResultRow>());
    }

    /// <summary>Clears the results.</summary>
    public void ClearResults() => LoadResults(Array.Empty<ValidationResult>());

    /// <summary>Shows which rules are skipped.</summary>
    public void SetSkippedRules(IEnumerable<string> codes)
    {
        ArgumentNullException.ThrowIfNull(codes);
        SkippedRules.Clear();
        foreach (string code in codes)
        {
            SkippedRules.Add(new SkippedRule(code, RuleName(code)));
        }

        OnPropertyChanged(nameof(HasSkippedRules));
    }

    /// <summary>The rows selected in the table (called by the view).</summary>
    public void SetSelection(IReadOnlyList<ValidationResultRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        _selection = rows;
        CanFixSelection = rows.Any(r => r.CanFix);
    }

    /// <summary>Raises a request to navigate to a result (called from the view after a double click).</summary>
    public void Activate(ValidationResultRow row) => EntryActivated?.Invoke(this, row.Result);

    /// <summary>"Fix": fixes the selected results that have a fix.</summary>
    public void FixSelected() => RequestFix(_selection);

    /// <summary>"Fix all": fixes every result that has a fix.</summary>
    public void FixAll() => RequestFix(Rows);

    /// <summary>"Skip this type of problem" for the rule of <paramref name="row"/>.</summary>
    public void SkipRule(ValidationResultRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.Result.Code.Length > 0)
        {
            SkipRuleRequested?.Invoke(this, row.Result.Code);
        }
    }

    /// <summary>Checks the given skipped rules again.</summary>
    public void RestoreRules(IReadOnlyList<string> codes)
    {
        ArgumentNullException.ThrowIfNull(codes);
        if (codes.Count > 0)
        {
            RestoreRulesRequested?.Invoke(this, codes);
        }
    }

    /// <summary>
    /// The name of a rule for the list of skipped rules: the text of its message with "…" in place of the
    /// details (the code is the message's resource key).
    /// </summary>
    public static string RuleName(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        string? message = code.Length > 0 ? CoreStrings.TryGet(code) : null;
        return string.IsNullOrEmpty(message) ? code : Placeholder().Replace(message, "…");
    }

    private void RequestFix(IEnumerable<ValidationResultRow> rows)
    {
        List<ValidationResult> fixable = rows.Where(r => r.CanFix).Select(r => r.Result).ToList();
        if (fixable.Count > 0)
        {
            FixRequested?.Invoke(this, fixable);
        }
    }

    [GeneratedRegex(@"\{\d+(:[^}]*)?\}")]
    private static partial Regex Placeholder();
}

/// <summary>A skipped rule of Check Book.</summary>
/// <param name="Code">The rule code.</param>
/// <param name="Name">The name shown to the user.</param>
public sealed record SkippedRule(string Code, string Name);

/// <summary>A row of the "Validation Results" panel table — a projection of <see cref="ValidationResult"/> for display.</summary>
public sealed class ValidationResultRow
{
    /// <summary>Creates a row from a validation result.</summary>
    public ValidationResultRow(ValidationResult result)
    {
        Result = result;
        int lastSlash = result.BookPath.LastIndexOf('/');
        FileName = lastSlash >= 0 ? result.BookPath[(lastSlash + 1)..] : result.BookPath;
    }

    /// <summary>The source result (the navigation target).</summary>
    public ValidationResult Result { get; }

    /// <summary>File name (without the path) to show in the "File" column.</summary>
    public string FileName { get; }

    /// <summary>Line number or "N/A".</summary>
    public string LineText => Result.Line > 0 ? Result.Line.ToString(System.Globalization.CultureInfo.InvariantCulture) : "N/A";

    /// <summary>Character offset or "N/A".</summary>
    public string OffsetText => Result.CharOffset >= 0
        ? Result.CharOffset.ToString(System.Globalization.CultureInfo.InvariantCulture)
        : "N/A";

    /// <summary>Whether the problem has an automatic fix.</summary>
    public bool CanFix => Result.Fix is not null;

    /// <summary>The "Fix" column: a check mark for a problem with an automatic fix.</summary>
    public string FixText => CanFix ? "✓" : string.Empty;

    /// <summary>The message.</summary>
    public string Message => Result.Message;
}
