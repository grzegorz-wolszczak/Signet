using System;
using System.Globalization;

namespace Signet.Core.Tests.TestSupport;

/// <summary>
/// A base for tests that check the English source data of the catalogs (MARC role names, languages, metadata
/// fields): for the duration of the test it sets the UI language to English so the result does not depend on
/// the environment culture. The translations are checked by separate localization tests.
/// </summary>
public abstract class EnglishUiCultureTest : IDisposable
{
    private readonly CultureInfo _previous = CultureInfo.CurrentUICulture;

    protected EnglishUiCultureTest() => CultureInfo.CurrentUICulture = new CultureInfo("en");

    /// <inheritdoc/>
    public void Dispose()
    {
        CultureInfo.CurrentUICulture = _previous;
        GC.SuppressFinalize(this);
    }
}
