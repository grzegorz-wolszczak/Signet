using System.ComponentModel;

namespace Signet.App.Resources;

/// <summary>
/// Source of XAML text bindings (<c>{loc:Loc Key}</c> → binding to <c>[Key]</c>): the indexer
/// returns the text in the current language, and <see cref="Refresh"/> raises an indexer change, so
/// all open windows switch language without a restart.
/// </summary>
public sealed class Localizer : INotifyPropertyChanged
{
    private Localizer()
    {
    }

    /// <summary>The single instance (the source of all <see cref="LocExtension"/> bindings).</summary>
    public static Localizer Instance { get; } = new();

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Text for the key in the current language.</summary>
    public string this[string key] => Strings.Get(key);

    /// <summary>
    /// Raises a change of all texts (after the language is switched). Avalonia 12 indexer bindings
    /// react to the name "Item" (verified by a test — neither "Item[]" nor an empty name is enough);
    /// "Item[]" is additionally raised for consumers following the WPF convention.
    /// </summary>
    public void Refresh()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }
}
