namespace Signet.EpubCheck.Core.Util;

/// <summary>Java's string operations where .NET's differ.</summary>
internal static class JavaStrings
{
    /// <summary>Java's <c>String.trim()</c>: removes the characters up to U+0020 at both ends (.NET's <c>Trim()</c> removes Unicode white space).</summary>
    public static string Trim(string value)
    {
        int start = 0;
        int end = value.Length;
        while (start < end && value[start] <= ' ')
        {
            start++;
        }

        while (end > start && value[end - 1] <= ' ')
        {
            end--;
        }

        return value[start..end];
    }
}
