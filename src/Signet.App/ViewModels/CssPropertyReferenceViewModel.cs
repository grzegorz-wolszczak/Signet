using System;
using System.Collections.Generic;
using System.Linq;
using Signet.Core.Parsers;

namespace Signet.App.ViewModels;

/// <summary>The read-only inherited and non-inherited CSS property lists shown by the reference window.</summary>
public sealed class CssPropertyReferenceViewModel : ViewModelBase
{
    private bool _showAll;
    private IReadOnlyList<string> _inheritedProperties = Array.Empty<string>();
    private IReadOnlyList<string> _nonInheritedProperties = Array.Empty<string>();

    /// <summary>Creates the reference with the common EPUB properties selected.</summary>
    public CssPropertyReferenceViewModel() => RefreshProperties();

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

    /// <summary>Inherited property names, sorted alphabetically.</summary>
    public IReadOnlyList<string> InheritedProperties
    {
        get => _inheritedProperties;
        private set => SetProperty(ref _inheritedProperties, value);
    }

    /// <summary>Non-inherited property names, sorted alphabetically.</summary>
    public IReadOnlyList<string> NonInheritedProperties
    {
        get => _nonInheritedProperties;
        private set => SetProperty(ref _nonInheritedProperties, value);
    }

    private void RefreshProperties()
    {
        IReadOnlyList<string> properties = ShowAll
            ? EpubCssPropertyCatalog.AllProperties
            : EpubCssPropertyCatalog.CommonProperties;

        InheritedProperties = Array.AsReadOnly(
            properties.Where(CssInheritedProperties.IsInherited)
                .OrderBy(property => property, StringComparer.OrdinalIgnoreCase)
                .ToArray());
        NonInheritedProperties = Array.AsReadOnly(
            properties.Where(property => !CssInheritedProperties.IsInherited(property))
                .OrderBy(property => property, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }
}
