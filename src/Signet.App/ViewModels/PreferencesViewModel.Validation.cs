using Signet.Core.BookManipulation;

namespace Signet.App.ViewModels;

/// <summary>Appearance › Validation: the sample panel of the preview.</summary>
public sealed partial class PreferencesViewModel
{
    private ValidationResultsViewModel? _validationSample;

    /// <summary>
    /// The Validation Results panel of the preview of its Appearance page: the real view model over fabricated results (some of
    /// them with a fix), where nothing is fixed, skipped or opened. Made when first asked for.
    /// </summary>
    public ValidationResultsViewModel ValidationSample => _validationSample ??= CreateValidationSample();

    // A fix that does nothing: the sample only needs results that have one (the "Fix" column and the buttons).
    private sealed class NoFix : ValidationFix
    {
        public override bool Apply(Book book) => false;
    }

    private static ValidationResultsViewModel CreateValidationSample()
    {
        ValidationResultsViewModel sample = new();
        sample.LoadResults(new[]
        {
            new ValidationResult(ValidationSeverity.Error, "OEBPS/Text/Chapter01.xhtml", 14, 523, "Element 'div' is not closed."),
            new ValidationResult(ValidationSeverity.Warning, "OEBPS/Text/Chapter01.xhtml", 18, 702, "The link target Missing.xhtml does not exist.", "Sample_BrokenLink"),
            new ValidationResult(ValidationSeverity.Warning, "OEBPS/Text/Chapter02.xhtml", 3, 61, "The file has no DOCTYPE.", "Sample_NoDoctype", new NoFix()),
            new ValidationResult(ValidationSeverity.Info, "OEBPS/Styles/Stylesheet.css", 6, 118, "The selector .lead is not used in the book."),
            new ValidationResult(ValidationSeverity.Warning, "OEBPS/Images/Figure01.png", 0, -1, "The image has no alt text in the files that use it.", "Sample_NoAlt", new NoFix()),
            new ValidationResult(ValidationSeverity.Error, "OEBPS/content.opf", 22, 984, "The manifest item cover.png has a wrong media type."),
        });
        return sample;
    }
}
