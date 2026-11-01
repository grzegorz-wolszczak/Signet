using System.Text;

namespace Signet.Core;

/// <summary>
/// The encodings of the .NET Framework days (<c>windows-125x</c>, <c>ISO-8859-x</c>, …): .NET itself knows only
/// Unicode, ASCII and Latin-1, the rest come from <see cref="CodePagesEncodingProvider"/>, which has to be registered
/// once in the process. Everything that reads a file whose encoding is named inside the file calls
/// <see cref="Ensure"/> first.
/// </summary>
/// <remarks>
/// Found in November 2026: a Hunspell dictionary says its encoding in the <c>SET</c> line of its <c>.aff</c> file, and
/// the Polish dictionary (as most non-Unicode ones) says <c>SET ISO8859-2</c>. Without the provider
/// <c>Encoding.GetEncoding</c> threw, the library fell back to Latin-1, and every word of the dictionary with a Polish
/// letter was read wrong (<c>ą</c>, byte 0xB1, became <c>±</c>): the whole dictionary was useless — correct words were
/// reported as misspelled and the suggestions came out as mojibake ("ksi±¿ki").
/// </remarks>
public static class LegacyEncodings
{
    private static readonly bool Registered = Register();

    /// <summary>
    /// Makes sure encodings such as <c>windows-1250</c> or <c>ISO-8859-2</c> can be resolved by name. Idempotent and
    /// thread-safe: the registration happens once, in the static initialization of this class.
    /// </summary>
    public static void Ensure()
    {
        // Forces the one-time static initialization (thread-safe, completed before the first read of the field) —
        // otherwise parallel callers could reach Encoding.GetEncoding before RegisterProvider has finished.
        _ = Registered;
    }

    private static bool Register()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return true;
    }
}
