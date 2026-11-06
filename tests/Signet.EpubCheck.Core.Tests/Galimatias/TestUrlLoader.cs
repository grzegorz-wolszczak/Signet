using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Signet.EpubCheck.Core.Galimatias;

namespace Signet.EpubCheck.Core.Tests.Galimatias;

/// <summary>One line of galimatias' URL test data: the input, its base, and the expected URL (<c>null</c>: must fail).</summary>
internal sealed record TestUrl(int Line, string RawUrl, Url? ParsedBaseUrl, Url? ParsedUrl)
{
    public override string ToString() => $"line {Line}: {RawUrl}";
}

/// <summary>Reads galimatias' URL test data as its <c>TestURLLoader</c> does (the format of web-platform-tests' urltestdata.txt).</summary>
internal static class TestUrlLoader
{
    public const string Whatwg = "urltestdata_whatwg.txt";
    public const string HostWhatwg = "urltestdata_host_whatwg.txt";

    private const string CommentPrefix = "--";
    private const int UnicodeEscapeLength = 4;
    private const int FieldPrefixLength = 2;

    public static List<TestUrl> Load(string dataset)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Galimatias", "TestData", dataset);
        List<TestUrl> results = [];
        string[] lines = File.ReadAllLines(path, Encoding.UTF8);
        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            string line = lines[lineIndex];
            if (line.Length == 0 || line.StartsWith(CommentPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            string[] fields = line.Split(' ');
            string rawUrl = Normalize(fields[0]);
            Url? baseUrl;
            if (fields.Length > 1 && fields[1].Length != 0)
            {
                baseUrl = Url.Parse(Normalize(fields[1]));
            }
            else
            {
                // As galimatias: the base of the line before.
                baseUrl = results[^1].ParsedBaseUrl;
            }

            if (fields.Length < 2)
            {
                results.Add(new TestUrl(lineIndex + 1, rawUrl, baseUrl, null));
                continue;
            }

            results.Add(new TestUrl(lineIndex + 1, rawUrl, baseUrl, Expected(fields)));
        }

        return results;
    }

    private static Url Expected(string[] fields)
    {
        string scheme = string.Empty;
        string username = string.Empty;
        string? password = null;
        Host? host = null;
        int port = -1;
        string? path = null;
        string? query = null;
        string? fragment = null;
        bool isHierarchical = false;
        for (int i = 2; i < fields.Length; i++)
        {
            string field = Normalize(fields[i]);
            string value = field[FieldPrefixLength..];
            if (field.StartsWith("s:", StringComparison.Ordinal))
            {
                scheme = value;
                isHierarchical = UrlUtils.IsRelativeScheme(scheme);
            }
            else if (field.StartsWith("u:", StringComparison.Ordinal))
            {
                username = value;
            }
            else if (field.StartsWith("pass:", StringComparison.Ordinal))
            {
                password = field["pass:".Length..];
            }
            else if (field.StartsWith("h:", StringComparison.Ordinal))
            {
                host = Host.ParseHost(value);
            }
            else if (field.StartsWith("port:", StringComparison.Ordinal))
            {
                port = int.Parse(field["port:".Length..], CultureInfo.InvariantCulture);
            }
            else if (field.StartsWith("p:", StringComparison.Ordinal))
            {
                path = value;
            }
            else if (field.StartsWith("q:", StringComparison.Ordinal))
            {
                query = value.StartsWith('?') ? value[1..] : value;
            }
            else if (field.StartsWith("f:", StringComparison.Ordinal))
            {
                fragment = value.StartsWith('#') ? value[1..] : value;
            }
        }

        string schemeData = string.Empty;
        if (!isHierarchical)
        {
            schemeData = path ?? string.Empty;
            path = null;
        }
        else if (UrlUtils.GetDefaultPortForScheme(scheme) == port)
        {
            port = -1;
        }

        return new Url(scheme, schemeData, username, password, host, port, path is null ? null : SplitPath(path), query, fragment, isHierarchical);
    }

    // The URL keeps the path as segments; "/a/b" -> ["a", "b"].
    private static List<string> SplitPath(string path) => [.. (path.StartsWith('/') ? path[1..] : path).Split('/')];

    // The escapes of urltestparser.js: \\ \n \r \s \t \f and \uXXXX.
    private static string Normalize(string input)
    {
        StringBuilder output = new(input.Length);
        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            if (c != '\\')
            {
                output.Append(c);
                continue;
            }

            i++;
            char next = i >= input.Length ? '\0' : input[i];
            switch (next)
            {
                case '\\':
                    output.Append('\\');
                    break;
                case 'n':
                    output.Append('\n');
                    break;
                case 'r':
                    output.Append('\r');
                    break;
                case 's':
                    output.Append(' ');
                    break;
                case 't':
                    output.Append('\t');
                    break;
                case 'f':
                    output.Append('\f');
                    break;
                case 'u':
                    output.Append((char)int.Parse(input.AsSpan(i + 1, UnicodeEscapeLength), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture));
                    i += UnicodeEscapeLength;
                    break;
                default:
                    throw new FormatException("Token error");
            }
        }

        return output.ToString();
    }
}
