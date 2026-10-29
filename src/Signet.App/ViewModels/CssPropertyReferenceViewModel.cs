using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Resources;
using Signet.Core.Parsers;

namespace Signet.App.ViewModels;

/// <summary>A CSS property, its display-context markers, and its localized tooltip.</summary>
public sealed record CssPropertyReferenceRow(string Display, string Name, string Tooltip);

/// <summary>The read-only inherited and non-inherited CSS property lists shown by the reference window.</summary>
public sealed class CssPropertyReferenceViewModel : ViewModelBase, ILanguageAware
{
    private static readonly (CssDisplayContext Context, string Marker)[] DisplayMarkers =
    {
        (CssDisplayContext.Inline, "i"),
        (CssDisplayContext.Block, "b"),
        (CssDisplayContext.InlineBlock, "ib"),
        (CssDisplayContext.Flex, "f"),
        (CssDisplayContext.Grid, "g"),
        (CssDisplayContext.Table, "t"),
    };

    private bool _showAll;
    private string _searchText = string.Empty;
    private IReadOnlyList<CssPropertyReferenceRow> _inheritedProperties = Array.Empty<CssPropertyReferenceRow>();
    private IReadOnlyList<CssPropertyReferenceRow> _nonInheritedProperties = Array.Empty<CssPropertyReferenceRow>();

    /// <summary>Creates the reference with the common EPUB properties selected.</summary>
    public CssPropertyReferenceViewModel()
    {
        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);
        Strings.RegisterLanguageAware(this);
        RefreshProperties();
    }

    /// <summary>Whether the lists contain all catalogued EPUB properties rather than the common subset.</summary>
    public bool ShowAll
    {
        get => _showAll;
        set
        {
            if (SetProperty(ref _showAll, value))
            {
                RefreshProperties();
            }
        }
    }

    /// <summary>Searches property names and display markers in both lists.</summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(HasSearchText));
                RefreshProperties();
            }
        }
    }

    /// <summary>Whether the search text is non-empty.</summary>
    public bool HasSearchText => !string.IsNullOrWhiteSpace(SearchText);

    /// <summary>Clears the search.</summary>
    public RelayCommand ClearSearchCommand { get; }

    /// <inheritdoc />
    public void OnLanguageChanged() => RefreshProperties();

    /// <summary>Inherited properties, sorted alphabetically.</summary>
    public IReadOnlyList<CssPropertyReferenceRow> InheritedProperties
    {
        get => _inheritedProperties;
        private set => SetProperty(ref _inheritedProperties, value);
    }

    /// <summary>Non-inherited properties, sorted alphabetically.</summary>
    public IReadOnlyList<CssPropertyReferenceRow> NonInheritedProperties
    {
        get => _nonInheritedProperties;
        private set => SetProperty(ref _nonInheritedProperties, value);
    }

    private void RefreshProperties()
    {
        IReadOnlyList<string> properties = ShowAll
            ? EpubCssPropertyCatalog.AllProperties
            : EpubCssPropertyCatalog.CommonProperties;
        string filter = SearchText.Trim();

        InheritedProperties = CreateRows(properties.Where(CssInheritedProperties.IsInherited), filter);
        NonInheritedProperties = CreateRows(
            properties.Where(property => !CssInheritedProperties.IsInherited(property)), filter);
    }

    private static ReadOnlyCollection<CssPropertyReferenceRow> CreateRows(IEnumerable<string> properties, string filter) =>
        Array.AsReadOnly(
            properties.OrderBy(property => property, StringComparer.OrdinalIgnoreCase)
                .Select(property => new { Name = property, Display = DisplayText(property) })
                .Where(row => filter.Length == 0
                    || row.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
                    || row.Display.Contains(filter, StringComparison.CurrentCultureIgnoreCase))
                .Select(row => new CssPropertyReferenceRow(
                    row.Display,
                    row.Name,
                    CssPropertyReferenceDocumentation.TooltipFor(row.Name)))
                .ToArray());

    private static string DisplayText(string property)
    {
        CssDisplayContext applicability = CssDisplayApplicability.ForProperty(property);
        string[] markers = DisplayMarkers
            .Where(item => (applicability & item.Context) != CssDisplayContext.None)
            .Select(item => item.Marker)
            .ToArray();
        return markers.Length == 0 ? "—" : string.Join(", ", markers);
    }
}
