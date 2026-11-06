namespace Signet.EpubCheck.Core.Util;

/// <summary>Code points and ranges of the Unicode standard that the checks name.</summary>
internal static class UnicodeRanges
{
    /// <summary>The last ASCII code point.</summary>
    public const int AsciiLast = 0x007F;

    /// <summary>C0 controls: U+0000 to U+001F.</summary>
    public const int C0ControlsLast = 0x001F;

    /// <summary>DELETE, the control at the end of ASCII.</summary>
    public const int Delete = 0x007F;

    /// <summary>C1 controls: U+0080 to U+009F.</summary>
    public const int C1ControlsFirst = 0x0080;

    /// <summary>The last C1 control.</summary>
    public const int C1ControlsLast = 0x009F;

    /// <summary>The last code point of Latin-1 (U+00FF).</summary>
    public const int Latin1Last = 0x00FF;

    /// <summary>The Private Use Area of the BMP: U+E000 to U+F8FF.</summary>
    public const int PrivateUseFirst = 0xE000;

    /// <summary>The last code point of the Private Use Area.</summary>
    public const int PrivateUseLast = 0xF8FF;

    /// <summary>Supplementary Private Use Area-A: U+F0000 to U+FFFFF.</summary>
    public const int SupplementaryPrivateUseAFirst = 0xF0000;

    /// <summary>The last code point of Supplementary Private Use Area-A.</summary>
    public const int SupplementaryPrivateUseALast = 0xFFFFF;

    /// <summary>Supplementary Private Use Area-B: U+100000 to U+10FFFF.</summary>
    public const int SupplementaryPrivateUseBFirst = 0x100000;

    /// <summary>The last code point of Supplementary Private Use Area-B (and of Unicode).</summary>
    public const int SupplementaryPrivateUseBLast = 0x10FFFF;

    /// <summary>The block of contiguous non characters: U+FDD0 to U+FDEF.</summary>
    public const int NonCharactersFirst = 0xFDD0;

    /// <summary>The last of the contiguous non characters.</summary>
    public const int NonCharactersLast = 0xFDEF;

    /// <summary>The last two code points of every plane (U+xFFFE, U+xFFFF) are non characters: the mask that finds them.</summary>
    public const int PlaneEndNonCharacterMask = 0xFFFE;

    /// <summary>The Specials block: U+FFF0 to U+FFFF.</summary>
    public const int SpecialsFirst = 0xFFF0;

    /// <summary>The last code point of the Specials block.</summary>
    public const int SpecialsLast = 0xFFFF;

    /// <summary>INTERLINEAR ANNOTATION ANCHOR.</summary>
    public const int InterlinearAnnotationAnchor = 0xFFF9;

    /// <summary>INTERLINEAR ANNOTATION SEPARATOR.</summary>
    public const int InterlinearAnnotationSeparator = 0xFFFA;

    /// <summary>INTERLINEAR ANNOTATION TERMINATOR.</summary>
    public const int InterlinearAnnotationTerminator = 0xFFFB;

    /// <summary>OBJECT REPLACEMENT CHARACTER.</summary>
    public const int ObjectReplacementCharacter = 0xFFFC;

    /// <summary>REPLACEMENT CHARACTER.</summary>
    public const int ReplacementCharacter = 0xFFFD;

    /// <summary>LANGUAGE TAG, deprecated.</summary>
    public const int LanguageTag = 0xE0001;
}
