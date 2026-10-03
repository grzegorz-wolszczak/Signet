namespace Signet.Core.Spellcheck;

/// <summary>The result of <see cref="SpellChecker.ValidateUserDictionaryName"/>.</summary>
public enum UserDictionaryNameProblem
{
    /// <summary>The name is valid.</summary>
    None,

    /// <summary>The name is empty (or only spaces).</summary>
    Empty,

    /// <summary>Characters not allowed in a file name, spaces at the edges or a trailing dot.</summary>
    InvalidCharacters,

    /// <summary>A dictionary with this name (case-insensitive) already exists.</summary>
    AlreadyExists,
}
