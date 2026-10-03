namespace Signet.Core.Search;

/// <summary>
/// A pair of offsets <c>[Start, End)</c> in a text. The offsets are in UTF-16 code units,
/// matching <see cref="string"/> indexing in .NET.
/// </summary>
/// <param name="Start">The start of the range (inclusive). <c>-1</c> if the group did not match.</param>
/// <param name="End">The end of the range (exclusive). <c>-1</c> if the group did not match.</param>
public readonly record struct SpcreCapture(int Start, int End)
{
    /// <summary>The length of the range (<c>End - Start</c>).</summary>
    public int Length => End - Start;
}
