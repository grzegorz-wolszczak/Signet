using System;
using System.IO;
using System.IO.Compression;
using WeCantSpell.Hunspell;
using Signet.Core.Localization;

namespace Signet.Core.Spellcheck;

/// <summary>
/// Loads the built-in (embedded resource) spelling dictionary. Currently the only built-in
/// dictionary is <c>en_US</c> (<c>EmbeddedData/dictionaries/en_US.aff</c> +
/// <c>en_US.dic.gz</c> — LibreOffice dictionaries / SCOWL, LGPL + BSD-style with an attribution
/// clause, see the <c>README_en_US.txt</c> and <c>COPYING_LGPL_v2.1.txt</c> files next to
/// the source resources). The user installs other languages themselves —
/// see <see cref="SpellChecker"/> and the <c>AppDirectories.HunspellDictionariesDirectory</c> directory.
/// </summary>
internal static class EmbeddedDictionary
{
    /// <summary>The name of the only built-in dictionary.</summary>
    public const string EnUsName = "en_US";

    /// <summary>Loads the built-in dictionary with the given name (currently only <see cref="EnUsName"/>).</summary>
    public static WordList Load(string name)
    {
        using Stream affStream = OpenResource($"Spellcheck.dictionary.{name}.aff");
        using Stream dicRaw = OpenResource($"Spellcheck.dictionary.{name}.dic");
        using GZipStream dicStream = new(dicRaw, CompressionMode.Decompress);
        return WordList.CreateFromStreams(dicStream, affStream);
    }

    private static Stream OpenResource(string logicalName)
    {
        return typeof(EmbeddedDictionary).Assembly.GetManifestResourceStream(logicalName)
            ?? throw new InvalidOperationException(CoreStrings.Format("Error_EmbeddedResourceMissing", logicalName));
    }
}
