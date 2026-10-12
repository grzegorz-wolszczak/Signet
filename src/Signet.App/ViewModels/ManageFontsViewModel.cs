using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Resources;
using Signet.Core.Fonts;
using Signet.Core.Resources;

namespace Signet.App.ViewModels;

/// <summary>
/// The "Manage Fonts" dialog: the font files of the book with their <c>@font-face</c> rules, the families with their
/// faces, how often they are used and the faces they lack, and the places of the selected family. The actions that need
/// a dialog (adding fonts, creating rules, deleting, renaming a family) are raised as events for the window.
/// </summary>
public sealed partial class ManageFontsViewModel : ObservableObject
{
    private readonly IFontManagementHost _host;
    private readonly Action<string, int>? _navigate;
    private IReadOnlyList<FontFileRow> _selectedFiles = Array.Empty<FontFileRow>();

    /// <summary>Creates the dialog and reads the book.</summary>
    public ManageFontsViewModel(IFontManagementHost host, Action<string, int>? navigate)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
        _navigate = navigate;
        Refresh();
    }

    /// <summary>Request: pick font files on disk and add them (the window shows the dialogs).</summary>
    public event EventHandler? AddFontsRequested;

    /// <summary>Request: show the "Add @font-face rules" dialog for these fonts.</summary>
    public event EventHandler<IReadOnlyList<FontResource>>? CreateRulesRequested;

    /// <summary>Request: delete these fonts (with the font removal window).</summary>
    public event EventHandler<IReadOnlyList<FontResource>>? DeleteRequested;

    /// <summary>Request: ask for the new name of this family.</summary>
    public event EventHandler<string>? RenameFamilyRequested;

    /// <summary>The font files.</summary>
    public ObservableCollection<FontFileRow> Files { get; } = new();

    /// <summary>The families: embedded ones first, then the ones only named.</summary>
    public ObservableCollection<FontFamilyRow> Families { get; } = new();

    /// <summary>The rules, usages and missing faces of the selected family.</summary>
    public ObservableCollection<FontLocationRow> Details { get; } = new();

    /// <summary>The selected family.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RenameFamilyCommand))]
    private FontFamilyRow? _selectedFamily;

    /// <summary>A one-line summary of the book's fonts.</summary>
    [ObservableProperty]
    private string _summary = string.Empty;

    /// <summary>The selected font files (set by the window).</summary>
    public IReadOnlyList<FontFileRow> SelectedFiles
    {
        get => _selectedFiles;
        set
        {
            _selectedFiles = value ?? Array.Empty<FontFileRow>();
            CreateRulesCommand.NotifyCanExecuteChanged();
            DeleteFilesCommand.NotifyCanExecuteChanged();
            OpenFilesCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>Reads the book again (after a change), keeping the selected family when it still exists.</summary>
    public void Refresh()
    {
        string? selected = SelectedFamily?.Report.Key;
        FontManagerReport? report = _host.BuildFontReport();
        Files.Clear();
        Families.Clear();
        Details.Clear();
        SelectedFiles = Array.Empty<FontFileRow>();
        if (report is null)
        {
            SelectedFamily = null;
            Summary = string.Empty;
            return;
        }

        foreach (FontFileReport file in report.Files)
        {
            Files.Add(new FontFileRow(file));
        }

        foreach (FontFamilyReport family in report.Families)
        {
            Families.Add(new FontFamilyRow(family));
        }

        SelectedFamily = Families.FirstOrDefault(f => string.Equals(f.Report.Key, selected, StringComparison.Ordinal))
            ?? Families.FirstOrDefault();
        Summary = Strings.Format(
            "ManageFonts_Summary",
            report.Files.Count,
            report.Families.Count(f => f.IsEmbedded),
            report.Files.Count(f => f.IsUnused),
            report.Families.Sum(f => f.Missing.Count));
    }

    /// <summary>Opens a place in the main window.</summary>
    public void Navigate(FontLocationRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        _navigate?.Invoke(row.BookPath, row.Offset);
    }

    partial void OnSelectedFamilyChanged(FontFamilyRow? value)
    {
        Details.Clear();
        if (value is null)
        {
            return;
        }

        FontFamilyReport family = value.Report;
        foreach (FontFaceRule rule in family.Rules)
        {
            string sources = string.Join(", ", rule.Sources.Select(s => s.Text));
            Details.Add(new FontLocationRow(
                "@font-face",
                string.Concat(FontFaceDescriptors.Describe(rule.Weight, rule.Style), " — ", sources),
                rule.BookPath,
                rule.Start));
        }

        foreach (MissingFontVariant missing in family.Missing)
        {
            Details.Add(new FontLocationRow(
                Strings.Get("ManageFonts_KindMissing"),
                Strings.Format("ManageFonts_MissingDetail", missing.Variant, missing.Elements),
                missing.BookPath,
                missing.Offset,
                IsWarning: true));
        }

        foreach (FontFamilyUsage usage in family.Usages)
        {
            Details.Add(new FontLocationRow(
                usage.Property,
                string.Concat(usage.Context, " — ", usage.Property, ": ", usage.Value),
                usage.BookPath,
                usage.DeclarationStart));
        }
    }

    [RelayCommand]
    private void AddFonts() => AddFontsRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand(CanExecute = nameof(HasSelectedFiles))]
    private void CreateRules() => CreateRulesRequested?.Invoke(this, SelectedFiles.Select(f => f.Report.Font).ToList());

    [RelayCommand(CanExecute = nameof(HasSelectedFiles))]
    private void DeleteFiles() => DeleteRequested?.Invoke(this, SelectedFiles.Select(f => f.Report.Font).ToList());

    [RelayCommand(CanExecute = nameof(HasSelectedFiles))]
    private void OpenFiles() => _host.OpenResources(SelectedFiles.Select(f => (Resource)f.Report.Font).ToList());

    [RelayCommand(CanExecute = nameof(CanRenameFamily))]
    private void RenameFamily() => RenameFamilyRequested?.Invoke(this, SelectedFamily!.Report.Family);

    [RelayCommand]
    private void Reload() => Refresh();

    private bool HasSelectedFiles => SelectedFiles.Count > 0;

    // Only an embedded family or one named somewhere can be renamed — both have places to change.
    private bool CanRenameFamily => SelectedFamily is { Report: { } report } && (report.Rules.Count > 0 || report.Usages.Count > 0);
}

/// <summary>A font file in "Manage Fonts".</summary>
public sealed class FontFileRow
{
    /// <summary>Wraps the report of one file.</summary>
    public FontFileRow(FontFileReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        Report = report;
        FontFaceCandidate candidate = report.Candidate;
        List<string> status = new();
        if (report.IsUnused)
        {
            status.Add(Strings.Get("ManageFonts_StatusNoRule"));
        }

        if (candidate.EmbeddingRestricted)
        {
            status.Add(Strings.Get("ManageFonts_StatusRestricted"));
        }

        if (candidate.FormatWarning == FontFormatWarning.NotInEpub2)
        {
            status.Add(Strings.Get("ManageFonts_StatusNotInEpub2"));
        }
        else if (candidate.FormatWarning == FontFormatWarning.Woff2)
        {
            status.Add(Strings.Get("ManageFonts_StatusWoff2"));
        }

        StatusText = string.Join("; ", status);
    }

    /// <summary>The report of the file.</summary>
    public FontFileReport Report { get; }

    /// <summary>The file name.</summary>
    public string FileName => Report.Candidate.FileName;

    /// <summary>The family the file declares.</summary>
    public string Family => Report.Candidate.Suggested.Family;

    /// <summary>The face the file declares (weight and style).</summary>
    public string Face => FontFaceDescriptors.Describe(
        FontFaceDescriptors.ParseWeight(Report.Candidate.Suggested.Weight), Report.Candidate.Suggested.Style);

    /// <summary>The file format.</summary>
    public string Format => Report.Candidate.Info.Format switch
    {
        FontFileFormat.TrueType => "TrueType",
        FontFileFormat.OpenTypeCff => "OpenType (CFF)",
        FontFileFormat.Collection => "TrueType Collection",
        FontFileFormat.Woff => "WOFF",
        FontFileFormat.Woff2 => "WOFF2",
        _ => "?",
    };

    /// <summary>The size in kilobytes.</summary>
    public string SizeText => (Report.Size / 1024.0).ToString("F0", CultureInfo.CurrentCulture) + " KB";

    /// <summary>The number of <c>@font-face</c> rules using the file.</summary>
    public int RuleCount => Report.Rules.Count;

    /// <summary>What deserves attention (empty when nothing).</summary>
    public string StatusText { get; }
}

/// <summary>A family in "Manage Fonts".</summary>
public sealed class FontFamilyRow
{
    /// <summary>Wraps the report of one family.</summary>
    public FontFamilyRow(FontFamilyReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        Report = report;
        List<string> status = new();
        if (!report.IsEmbedded)
        {
            status.Add(Strings.Get("ManageFonts_StatusNotEmbedded"));
        }
        else if (report.IsUnused)
        {
            status.Add(Strings.Get("ManageFonts_StatusUnused"));
        }

        status.AddRange(report.Missing.Select(m => Strings.Format("ManageFonts_StatusMissing", m.Variant, m.Elements)));
        StatusText = string.Join("; ", status);
    }

    /// <summary>The report of the family.</summary>
    public FontFamilyReport Report { get; }

    /// <summary>The family name.</summary>
    public string Family => Report.Family;

    /// <summary>The faces, or a dash for a family that is not embedded.</summary>
    public string Faces => Report.IsEmbedded ? Report.FacesText : "—";

    /// <summary>The number of declarations naming the family.</summary>
    public int Declarations => Report.Usages.Count;

    /// <summary>The number of elements with text rendered in the family.</summary>
    public int Elements => Report.Elements;

    /// <summary>What deserves attention (empty when nothing).</summary>
    public string StatusText { get; }
}

/// <summary>A place of a family: a rule, a usage or an element asking for a missing face.</summary>
/// <param name="Kind">What the place is.</param>
/// <param name="Text">A description.</param>
/// <param name="BookPath">The file.</param>
/// <param name="Offset">The offset in the file.</param>
/// <param name="IsWarning">Whether the place is a problem (a missing face).</param>
public sealed record FontLocationRow(string Kind, string Text, string BookPath, int Offset, bool IsWarning = false)
{
    /// <summary>The file name.</summary>
    public string FileText => Path.GetFileName(BookPath);
}
