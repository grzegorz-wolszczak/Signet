using System;
using System.Globalization;

namespace Signet.App.Tests.TestSupport;

/// <summary>
/// Sets <see cref="CultureInfo.CurrentUICulture"/> for the duration of a test and restores the previous
/// one. UI texts (menus, actions) depend on the language; without this the test result would depend
/// on the system language.
/// </summary>
public sealed class UiCultureScope : IDisposable
{
    private readonly CultureInfo _previous = CultureInfo.CurrentUICulture;

    /// <summary>Switches the UI language to <paramref name="name"/> (e.g. <c>en</c>, <c>pl</c>).</summary>
    public UiCultureScope(string name) => CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);

    /// <inheritdoc />
    public void Dispose() => CultureInfo.CurrentUICulture = _previous;
}
