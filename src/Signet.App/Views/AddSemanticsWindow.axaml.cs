using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Signet.Core.Semantics;

namespace Signet.App.Views;

/// <summary>
/// The "Add Semantics" window: a list of the available semantic codes (EPUB 2 guide /
/// EPUB 3 landmarks) with a description and the current code selected.
/// A single choice rather than a multi-select list — only the first selected entry would be used anyway.
/// </summary>
public partial class AddSemanticsWindow : Window
{
    private readonly Dictionary<string, string> _labelToCode = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _codeToDescription = new(StringComparer.Ordinal);

    /// <summary>Initializes the window.</summary>
    public AddSemanticsWindow()
    {
        InitializeComponent();
        CodesList.SelectionChanged += (_, _) => UpdateDescription();
        CodesList.DoubleTapped += (_, _) => Close(SelectedCode());
        OkButton.Click += (_, _) => Close(SelectedCode());
        CancelButton.Click += (_, _) => Close(null);
    }

    /// <summary>
    /// Shows the window; returns the chosen semantic code (or <c>null</c> after cancelling / without a choice).
    /// Choosing a code identical to <paramref name="currentCode"/> toggles it — removes the semantics.
    /// </summary>
    public static async Task<string?> AskAsync(
        Window owner, string currentCode, IReadOnlyDictionary<string, DescriptiveInfo> codeMap)
    {
        AddSemanticsWindow window = new();
        window.Populate(currentCode, codeMap);
        return await window.ShowDialog<string?>(owner);
    }

    private void Populate(string currentCode, IReadOnlyDictionary<string, DescriptiveInfo> codeMap)
    {
        List<string> labels = new(codeMap.Count);
        foreach ((string code, DescriptiveInfo info) in codeMap)
        {
            string label = $"{info.Name} ({code})";
            if (string.Equals(code, currentCode, StringComparison.Ordinal))
            {
                label += " ✓";
            }

            _labelToCode[label] = code;
            _codeToDescription[code] = info.Description;
            labels.Add(label);
        }

        labels.Sort(StringComparer.Ordinal);
        CodesList.ItemsSource = labels;
    }

    private string? SelectedCode() =>
        CodesList.SelectedItem is string label ? _labelToCode.GetValueOrDefault(label) : null;

    private void UpdateDescription()
    {
        string? code = SelectedCode();
        DescriptionText.Text = code is not null ? _codeToDescription.GetValueOrDefault(code, string.Empty) : string.Empty;
    }
}
