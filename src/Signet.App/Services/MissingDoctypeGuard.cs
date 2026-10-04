using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Signet.Core.Misc;
using Signet.Core.Resources;

namespace Signet.App.Services;

/// <summary>The user's answer in the "missing DOCTYPE" warning dialog.</summary>
public enum MissingDoctypeAnswer
{
    /// <summary>Cancel the operation (also: the dialog was closed without an answer).</summary>
    Cancel,

    /// <summary>Continue this time; ask again next time.</summary>
    Continue,

    /// <summary>Continue and never ask again (<see cref="SettingsStore.WarnMissingDoctype"/> is turned off).</summary>
    ContinueAndDontAskAgain,
}

/// <summary>The "missing DOCTYPE" warning dialog. Implemented by the main window view.</summary>
public interface IMissingDoctypePrompt
{
    /// <summary>Asks whether to run <paramref name="operationName"/> although the listed files have no DOCTYPE.</summary>
    /// <param name="operationName">Translated name of the operation (dialog header).</param>
    /// <param name="fileNames">Book paths of the files without a DOCTYPE.</param>
    Task<MissingDoctypeAnswer> AskAsync(string operationName, IReadOnlyList<string> fileNames);
}

/// <summary>
/// Confirmation before operations that rewrite (X)HTML files when some of them have no DOCTYPE.
/// A missing DOCTYPE is not an error (calibre often omits it and reading systems accept such files),
/// so instead of blocking the operation the user is warned and decides. "Don't ask again" is
/// remembered only together with "Yes" — remembering it with "No" would silently block the operation
/// forever, which is exactly what this warning replaces.
/// </summary>
public sealed class MissingDoctypeGuard
{
    private readonly SettingsStore _settings;

    /// <summary>Creates the guard.</summary>
    public MissingDoctypeGuard(SettingsStore settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <summary>The dialog; while <c>null</c> (headless tests, before the window opens) operations run without asking.</summary>
    public IMissingDoctypePrompt? Prompt { get; set; }

    /// <summary>
    /// Asks for confirmation when <paramref name="missing"/> is not empty and the warning is enabled
    /// (<see cref="SettingsStore.WarnMissingDoctype"/>). Returns <c>true</c> when the operation may run.
    /// </summary>
    public Task<bool> ConfirmAsync(string operationName, IReadOnlyList<HtmlResource> missing)
    {
        ArgumentNullException.ThrowIfNull(missing);
        return ConfirmAsync(operationName, missing.Select(h => h.BookPath).ToList());
    }

    /// <inheritdoc cref="ConfirmAsync(string, IReadOnlyList{HtmlResource})"/>
    public async Task<bool> ConfirmAsync(string operationName, IReadOnlyList<string> fileNames)
    {
        ArgumentNullException.ThrowIfNull(operationName);
        ArgumentNullException.ThrowIfNull(fileNames);
        if (fileNames.Count == 0 || !_settings.WarnMissingDoctype || Prompt is null)
        {
            return true;
        }

        MissingDoctypeAnswer answer = await Prompt.AskAsync(operationName, fileNames).ConfigureAwait(true);
        if (answer == MissingDoctypeAnswer.ContinueAndDontAskAgain)
        {
            _settings.WarnMissingDoctype = false;
            _settings.Save();
        }

        return answer != MissingDoctypeAnswer.Cancel;
    }
}
