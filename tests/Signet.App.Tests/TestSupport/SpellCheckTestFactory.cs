using System;
using Signet.Core.Misc;
using Signet.Core.Spellcheck;
using Signet.Core.Tests.TestSupport;

namespace Signet.App.Tests.TestSupport;

/// <summary>
/// Builds an isolated <see cref="SettingsStore"/> + <see cref="SpellChecker"/> (in a temporary folder of their own;
/// <see cref="SpellChecker"/> has a bundled <c>en_US</c> anyway) for App view/view model tests that need the spellcheck
/// engine only as a constructor dependency, not as the subject of the test.
/// </summary>
internal static class SpellCheckTestFactory
{
    /// <summary>Creates a new settings + spellcheck engine pair; disposing it deletes their folder.</summary>
    public static SpellCheckSetup New() => new();
}

/// <summary>
/// Settings and a spellcheck engine in a temporary folder of their own, deleted on <see cref="Dispose"/>
/// (<c>using SpellCheckSetup spell = SpellCheckTestFactory.New(); (SettingsStore settings, SpellChecker spellChecker) = spell;</c>).
/// </summary>
internal sealed class SpellCheckSetup : IDisposable
{
    private readonly TempDir _folder = new("spellcheck-");

    public SpellCheckSetup()
    {
        Settings = new SettingsStore(_folder.Combine("settings.xml"));
        HunspellDirectory = _folder.Combine("hunspell_dictionaries");
        SpellChecker = new SpellChecker(Settings, HunspellDirectory, _folder.Combine("user_dictionaries"));
    }

    /// <summary>
    /// The directory the engine looks for installed dictionaries in — a test that needs one of its own writes it
    /// there (<c>DictionaryFixture</c>) and calls <see cref="SpellChecker.Reload"/>.
    /// </summary>
    public string HunspellDirectory { get; }

    /// <summary>The settings.</summary>
    public SettingsStore Settings { get; }

    /// <summary>The spellcheck engine over <see cref="Settings"/>.</summary>
    public SpellChecker SpellChecker { get; }

    /// <summary>Gives the settings and the engine (<c>(SettingsStore settings, SpellChecker spellChecker) = spell;</c>).</summary>
    public void Deconstruct(out SettingsStore settings, out SpellChecker spellChecker)
    {
        settings = Settings;
        spellChecker = SpellChecker;
    }

    /// <inheritdoc />
    public void Dispose() => _folder.Dispose();
}
