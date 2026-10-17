using Signet.Core.Footnotes;

namespace Signet.App.ViewModels;

/// <summary>Appearance › Footnotes: the sample footnote window of the preview.</summary>
public sealed partial class PreferencesViewModel
{
    private FootnoteViewModel? _footnoteSample;

    /// <summary>
    /// The footnote window of the preview of its Appearance page: the real view model for a footnote added after a word of a
    /// sentence, with the text of the note filled in. Made when first asked for.
    /// </summary>
    public FootnoteViewModel FootnoteSample => _footnoteSample ??= CreateFootnoteSample();

    private static FootnoteViewModel CreateFootnoteSample()
    {
        const string text = "<html><head><title>t</title></head><body><p>The sea was calm that morning, and the keeper climbed the stairs.</p></body></html>";
        return new FootnoteViewModel(
            text, text.IndexOf(" that", System.StringComparison.Ordinal), FootnotePlacement.Local, 1, 1,
            content => FootnoteEditor.ValidateContent(content, "3.0"))
        {
            Content = "The lamp was lit every dusk.",
        };
    }
}
