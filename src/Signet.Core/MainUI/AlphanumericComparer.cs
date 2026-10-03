using System.Collections.Generic;
using System.Globalization;

namespace Signet.Core.MainUI;

/// <summary>
/// "Natural" string comparison (digits compared numerically, the rest character by character,
/// letter case significant). Used by the "Sort" actions for text files
/// in <see cref="OpfModel"/>.
/// </summary>
public sealed class AlphanumericComparer : IComparer<string>
{
    /// <summary>A shared, stateless instance.</summary>
    public static AlphanumericComparer Instance { get; } = new();

    /// <inheritdoc />
    public int Compare(string? x, string? y)
    {
        string s1 = x ?? string.Empty;
        string s2 = y ?? string.Empty;

        if (s1.Length == 0 || s2.Length == 0)
        {
            // When either key is empty, the element is not "smaller"; empty keys sort last,
            // and two empty keys compare as equal (as a stable IComparer requires).
            return s1.Length == 0 && s2.Length == 0 ? 0 : (s1.Length == 0 ? 1 : -1);
        }

        int len1 = s1.Length;
        int len2 = s2.Length;
        int marker1 = 0;
        int marker2 = 0;

        while (marker1 < len1 && marker2 < len2)
        {
            char ch1 = s1[marker1];
            char ch2 = s2[marker2];

            System.Text.StringBuilder chunk1 = new();
            System.Text.StringBuilder chunk2 = new();

            bool digits1 = char.IsDigit(ch1);
            do
            {
                chunk1.Append(ch1);
                marker1++;
                if (marker1 >= len1)
                {
                    break;
                }

                ch1 = s1[marker1];
            }
            while (char.IsDigit(ch1) == digits1);

            bool digits2 = char.IsDigit(ch2);
            do
            {
                chunk2.Append(ch2);
                marker2++;
                if (marker2 >= len2)
                {
                    break;
                }

                ch2 = s2[marker2];
            }
            while (char.IsDigit(ch2) == digits2);

            string str1 = chunk1.ToString();
            string str2 = chunk2.ToString();

            int result;
            if (digits1 && digits2)
            {
                result = long.TryParse(str1, NumberStyles.None, CultureInfo.InvariantCulture, out long n1)
                         && long.TryParse(str2, NumberStyles.None, CultureInfo.InvariantCulture, out long n2)
                    ? n1.CompareTo(n2)
                    : string.CompareOrdinal(str1, str2);
            }
            else
            {
                result = string.CompareOrdinal(str1, str2);
            }

            if (result != 0)
            {
                return result;
            }
        }

        return len1.CompareTo(len2);
    }
}
