using System;
using System.Globalization;
using System.Text;

namespace Signet.EpubCheck.Core.Messages;

/// <summary>
/// The part of Java's <c>String.format</c> that the message texts use: <c>%1$s</c> (an argument by position), <c>%s</c>
/// (the next argument), <c>%d</c> (an integer), <c>%%</c> and <c>%n</c>. The texts of upstream, and the custom messages files people write
/// for it, use this syntax, so it is kept instead of .NET's <c>{0}</c>.
/// </summary>
public static class JavaFormat
{
    /// <summary>Fills the placeholders of <paramref name="template"/> with <paramref name="args"/>.</summary>
    /// <exception cref="FormatException">The template uses a conversion that is not supported, or an argument that was not given.</exception>
    public static string Format(string template, params object?[] args)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(args);

        StringBuilder result = new(template.Length + 16);
        int ordinary = 0;
        int i = 0;
        while (i < template.Length)
        {
            char c = template[i];
            if (c != '%')
            {
                result.Append(c);
                i++;
                continue;
            }

            int start = i;
            i++;
            int number = 0;
            int digits = 0;
            while (i < template.Length && char.IsAsciiDigit(template[i]))
            {
                number = (number * 10) + (template[i] - '0');
                digits++;
                i++;
            }

            bool indexed = digits > 0 && i < template.Length && template[i] == '$';
            if (indexed)
            {
                i++;
            }
            else
            {
                // Not an index: whatever digits came are not a supported flag or width.
                if (digits > 0)
                {
                    throw new FormatException($"Unknown format specifier '{template[start..Math.Min(i + 1, template.Length)]}'");
                }
            }

            if (i >= template.Length)
            {
                throw new FormatException($"Unknown format specifier '{template[start..]}'");
            }

            char conversion = template[i++];
            switch (conversion)
            {
                case 's':
                    int index = indexed ? number - 1 : ordinary++;
                    if (index < 0 || index >= args.Length)
                    {
                        throw new FormatException($"Format specifier '{template[start..i]}'");
                    }

                    result.Append(ToText(args[index]));
                    break;
                case 'd':
                    int numberIndex = indexed ? number - 1 : ordinary++;
                    if (numberIndex < 0 || numberIndex >= args.Length)
                    {
                        throw new FormatException($"Format specifier '{template[start..i]}'");
                    }

                    result.Append(args[numberIndex] is int or long or short or byte
                        ? Convert.ToString(args[numberIndex], CultureInfo.InvariantCulture)
                        : throw new FormatException("d != " + (args[numberIndex]?.GetType().FullName ?? "null")));
                    break;
                case '%' when !indexed:
                    result.Append('%');
                    break;
                case 'n' when !indexed:
                    result.Append('\n');
                    break;
                default:
                    throw new FormatException($"Conversion = '{conversion}'");
            }
        }

        return result.ToString();
    }

    private static string ToText(object? argument) => argument switch
    {
        null => "null",
        bool flag => flag ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => argument.ToString() ?? "null",
    };
}
