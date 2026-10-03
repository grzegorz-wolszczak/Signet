using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Signet.Core.Semantics;

namespace Signet.App.Views;

/// <summary>
/// A generic dialog for picking a metadata field/property, reused
/// in the "Metadata Editor" for four purposes: "Add Metadata Element", "Add Property to
/// Element", "Select Language" and "Select Role" (with the <c>MarcRelators</c> map). A multi-select
/// list of sorted display names, with a description panel below.
/// </summary>
public partial class PickMetadataFieldWindow : Window
{
    private readonly Dictionary<string, string> _labelToCode = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _codeToDescription = new(StringComparer.Ordinal);

    /// <summary>Initializes the window.</summary>
    public PickMetadataFieldWindow()
    {
        InitializeComponent();
        OptionsList.SelectionChanged += (_, _) => UpdateDescription();
        OptionsList.DoubleTapped += (_, _) => Close(SelectedCodes());
        OkButton.Click += (_, _) => Close(SelectedCodes());
        CancelButton.Click += (_, _) => Close(Array.Empty<string>());
    }

    /// <summary>
    /// Shows the dialog and returns the codes of the chosen entries (empty after cancelling / without a choice).
    /// </summary>
    public static async Task<IReadOnlyList<string>> AskAsync(
        Window owner, string title, IReadOnlyDictionary<string, DescriptiveInfo> options)
    {
        PickMetadataFieldWindow window = new() { Title = title };
        window.Populate(options);
        return await window.ShowDialog<IReadOnlyList<string>?>(owner) ?? Array.Empty<string>();
    }

    /// <summary>Like <see cref="AskAsync"/>, but returns only the first chosen code (or <c>null</c>).</summary>
    public static async Task<string?> AskOneAsync(
        Window owner, string title, IReadOnlyDictionary<string, DescriptiveInfo> options)
    {
        IReadOnlyList<string> codes = await AskAsync(owner, title, options);
        return codes.Count > 0 ? codes[0] : null;
    }

    private void Populate(IReadOnlyDictionary<string, DescriptiveInfo> options)
    {
        List<string> labels = new(options.Count);
        foreach ((string code, DescriptiveInfo info) in options)
        {
            _labelToCode[info.Name] = code;
            _codeToDescription[code] = info.Description;
            labels.Add(info.Name);
        }

        labels.Sort(StringComparer.Ordinal);
        OptionsList.ItemsSource = labels;
    }

    private IReadOnlyList<string> SelectedCodes() =>
        OptionsList.SelectedItems is { } items
            ? items.OfType<string>().Select(label => _labelToCode.GetValueOrDefault(label, label)).ToList()
            : Array.Empty<string>();

    private void UpdateDescription()
    {
        string? code = OptionsList.SelectedItem is string label ? _labelToCode.GetValueOrDefault(label) : null;
        DescriptionText.Text = code is not null ? _codeToDescription.GetValueOrDefault(code, string.Empty) : string.Empty;
    }
}
