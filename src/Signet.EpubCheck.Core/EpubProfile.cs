using System.Globalization;

namespace Signet.EpubCheck.Core;

/// <summary>The validation profile of EPUB 3: a specification that extends the default checks (<c>--profile</c>).</summary>
public enum EpubProfile
{
    /// <summary>The default profile.</summary>
    Default = 0,

    /// <summary>EPUB Dictionaries and Glossaries.</summary>
    Dict,

    /// <summary>The EDUPUB profile.</summary>
    Edupub,

    /// <summary>EPUB Indexes.</summary>
    Idx,

    /// <summary>EPUB Previews.</summary>
    Preview,
}

/// <summary>The names of the <see cref="EpubProfile"/>s on the command line.</summary>
public static class EpubProfiles
{
    /// <summary>
    /// Parses the name of a profile (<c>default</c>, <c>dict</c>, <c>edupub</c>, <c>idx</c>, <c>preview</c>), in any letter
    /// case, as upstream does; <c>false</c> when the name is none of them.
    /// </summary>
    public static bool TryParse(string? name, out EpubProfile profile)
    {
        (bool known, profile) = name?.ToUpperInvariant() switch
        {
            "DEFAULT" => (true, EpubProfile.Default),
            "DICT" => (true, EpubProfile.Dict),
            "EDUPUB" => (true, EpubProfile.Edupub),
            "IDX" => (true, EpubProfile.Idx),
            "PREVIEW" => (true, EpubProfile.Preview),
            _ => (false, EpubProfile.Default),
        };
        return known;
    }

    /// <summary>The name of the profile as the command line spells it (lower case).</summary>
    public static string ToText(this EpubProfile profile) => profile.ToString().ToLower(CultureInfo.InvariantCulture);
}
