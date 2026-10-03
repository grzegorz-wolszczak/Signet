using System;
using System.IO;
using Signet.Core.Misc;
using Signet.Core.Spellcheck;

namespace Signet.App.Tests.TestSupport;

/// <summary>
/// Builds an isolated <see cref="SettingsStore"/> + <see cref="SpellChecker"/> (temporary,
/// non-existent dictionary folder paths; <see cref="SpellChecker"/> has a bundled <c>en_US</c>
/// anyway) for App view/view model tests that need the spellcheck engine only as a constructor
/// dependency, not as the subject of the test.
/// </summary>
internal static class SpellCheckTestFactory
{
    /// <summary>Creates a new settings + spellcheck engine pair on unique temporary paths.</summary>
    public static (SettingsStore Settings, SpellChecker SpellChecker) New()
    {
        string root = Path.Combine(Path.GetTempPath(), "Signet.Tests", "spellcheck-" + Guid.NewGuid().ToString("N"));
        SettingsStore settings = new(Path.Combine(root, "settings.json"));
        SpellChecker spellChecker = new(
            settings,
            Path.Combine(root, "hunspell_dictionaries"),
            Path.Combine(root, "user_dictionaries"));
        return (settings, spellChecker);
    }
}
