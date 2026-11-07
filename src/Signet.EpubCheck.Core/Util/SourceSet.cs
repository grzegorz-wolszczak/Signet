using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Signet.EpubCheck.Core.Util;

/// <summary>An image source of a source set (upstream's <c>SourceSet.ImageSource</c>): a URL with a width or a density descriptor.</summary>
/// <param name="Url">The URL of the image.</param>
/// <param name="Width">The width descriptor, or <c>null</c>.</param>
/// <param name="Density">The density descriptor, or <c>null</c>.</param>
public sealed record ImageSource(string Url, int? Width, float? Density);

/// <summary>The errors of parsing a <c>srcset</c> value (upstream's <c>SourceSet.ParseError</c>).</summary>
public enum SourceSetParseError
{
    /// <summary>The value is <c>null</c> or empty.</summary>
    NullOrEmpty,

    /// <summary>The value starts with a comma.</summary>
    EmptyStart,

    /// <summary>A comma follows a comma.</summary>
    EmptyMiddle,

    /// <summary>A width and a density descriptor are mixed.</summary>
    DescriptorMixWidthDensity,

    /// <summary>More than one density descriptor.</summary>
    DescriptorDensityMoreThanOnce,

    /// <summary>A negative density.</summary>
    DescriptorDensityNegative,

    /// <summary>A density that is not a floating-point number.</summary>
    DescriptorDensityNotFloat,

    /// <summary>More than one width descriptor.</summary>
    DescriptorWidthMoreThanOnce,

    /// <summary>A width with a sign.</summary>
    DescriptorWidthSigned,

    /// <summary>A width that is not an integer.</summary>
    DescriptorWidthNotInt,

    /// <summary>A width of zero.</summary>
    DescriptorWidthZero,

    /// <summary>A height of zero.</summary>
    DescriptorFutureHZero,

    /// <summary>More than one height descriptor.</summary>
    DescriptorFutureHMoreThanOnce,

    /// <summary>A height that is not an integer.</summary>
    DescriptorFutureHNotInt,

    /// <summary>A height without a width.</summary>
    DescriptorFutureHWithoutWidth,

    /// <summary>A height with a density.</summary>
    DescriptorFutureHWithDensity,

    /// <summary>A descriptor with a suffix that is not <c>w</c>, <c>x</c> or <c>h</c>.</summary>
    DescriptorUnknownSuffix,
}

/// <summary>
/// An image source set (upstream's <c>SourceSet</c>, <see href="https://html.spec.whatwg.org/#source-set"/>), parsed from a
/// <c>srcset</c> value by the "parsing a srcset attribute" algorithm.
/// </summary>
public sealed partial class SourceSet
{
    private SourceSet(IReadOnlyList<ImageSource> sources)
    {
        ImageSources = sources;
    }

    /// <summary>The image sources.</summary>
    public IReadOnlyList<ImageSource> ImageSources { get; }

    /// <summary>The URLs of the image sources.</summary>
    public IReadOnlyList<string> ImageUrls => [.. ImageSources.Select(source => source.Url)];

    /// <summary>Whether the set has no source.</summary>
    public bool IsEmpty => ImageSources.Count == 0;

    /// <summary>Parses <paramref name="srcset"/>; <paramref name="errorHandler"/> gets the errors (the position is -1 when it is not known).</summary>
    public static SourceSet Parse(string? srcset, Action<SourceSetParseError, int>? errorHandler = null) =>
        new Parser(errorHandler).Parse(srcset);

    [GeneratedRegex(@"^(\d+(\.\d+)?|\.\d+)([eE][-+]?\d+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex PositiveFloat();

    private enum State
    {
        Split,
        CollectUrl,
        TokenizeDescriptors,
        InDescriptor,
        AfterDescriptor,
        InParens,
    }

    // Follows the logic of upstream's parser, which follows the algorithm of the HTML standard.
    private sealed class Parser(Action<SourceSetParseError, int>? errorHandler)
    {
        public SourceSet Parse(string? srcset)
        {
            List<ImageSource> sources = [];
            if (string.IsNullOrEmpty(srcset))
            {
                Error(SourceSetParseError.NullOrEmpty, -1);
                return new SourceSet(sources);
            }

            List<string> descriptors = [];
            StringBuilder url = new();
            StringBuilder descriptor = new();
            State state = State.Split;
            int position = 0;
            bool consume = true;
            char c = ' ';
            while (!consume || position < srcset.Length)
            {
                if (consume)
                {
                    c = srcset[position++];
                }
                else
                {
                    consume = true;
                }

                switch (state)
                {
                    case State.Split:
                        if (CodePoints.IsAsciiWhitespace(c))
                        {
                            // skip white space
                        }
                        else if (c == ',')
                        {
                            Error(sources.Count == 0 ? SourceSetParseError.EmptyStart : SourceSetParseError.EmptyMiddle, position);
                        }
                        else
                        {
                            state = State.CollectUrl;
                            consume = false;
                        }

                        break;
                    case State.CollectUrl:
                        if (CodePoints.IsAsciiWhitespace(c))
                        {
                            if (url[^1] == ',')
                            {
                                Add(sources, FinalizeSource(url, descriptors));
                                state = State.Split;
                            }
                            else
                            {
                                state = State.TokenizeDescriptors;
                            }
                        }
                        else
                        {
                            url.Append(c);
                        }

                        break;
                    case State.TokenizeDescriptors:
                        if (!CodePoints.IsAsciiWhitespace(c))
                        {
                            state = State.InDescriptor;
                            consume = false;
                        }

                        break;
                    case State.InDescriptor:
                        if (CodePoints.IsAsciiWhitespace(c))
                        {
                            FlushDescriptor(descriptors, descriptor);
                            state = State.AfterDescriptor;
                        }
                        else if (c == ',')
                        {
                            FlushDescriptor(descriptors, descriptor);
                            Add(sources, FinalizeSource(url, descriptors));
                            state = State.Split;
                        }
                        else
                        {
                            descriptor.Append(c);
                            if (c == '(')
                            {
                                state = State.InParens;
                            }
                        }

                        break;
                    case State.InParens:
                        descriptor.Append(c);
                        if (c == ')')
                        {
                            state = State.InDescriptor;
                        }

                        break;
                    case State.AfterDescriptor:
                        if (!CodePoints.IsAsciiWhitespace(c))
                        {
                            state = State.InDescriptor;
                            consume = false;
                        }

                        break;
                }
            }

            // add the latest descriptor if not already added (end of input)
            if (descriptor.Length > 0)
            {
                descriptors.Add(descriptor.ToString());
            }

            Add(sources, FinalizeSource(url, descriptors));
            return new SourceSet(sources);
        }

        private static void Add(List<ImageSource> sources, ImageSource? source)
        {
            if (source is not null)
            {
                sources.Add(source);
            }
        }

        private static void FlushDescriptor(List<string> descriptors, StringBuilder descriptor)
        {
            if (descriptor.Length > 0)
            {
                descriptors.Add(descriptor.ToString());
                descriptor.Clear();
            }
        }

        private ImageSource? FinalizeSource(StringBuilder url, List<string> descriptors)
        {
            if (url.Length == 0)
            {
                return null;
            }

            int trailingCommas = 0;
            while (url.Length > 0 && url[^1] == ',')
            {
                url.Length--;
                trailingCommas++;
            }

            if (trailingCommas > 1)
            {
                Error(SourceSetParseError.EmptyMiddle, -1);
            }

            SourceSetParseError? error = null;
            int width = -1;
            float density = -1;
            int futureH = -1;
            foreach (string descriptor in descriptors)
            {
                char last = descriptor[^1];
                char first = descriptor[0];
                string value = descriptor[..^1];
                if (first is '-' or '+')
                {
                    error = SourceSetParseError.DescriptorWidthSigned;
                }
                else if (last == 'w')
                {
                    if (width != -1 || density != -1)
                    {
                        error = width != -1 ? SourceSetParseError.DescriptorWidthMoreThanOnce : SourceSetParseError.DescriptorMixWidthDensity;
                    }
                    else if (TryParseInt(value, out int parsedWidth))
                    {
                        width = parsedWidth;
                        if (width == 0)
                        {
                            error = SourceSetParseError.DescriptorWidthZero;
                            width = -1;
                        }
                    }
                    else
                    {
                        error = SourceSetParseError.DescriptorWidthNotInt;
                    }
                }
                else if (last == 'x')
                {
                    if (width != -1 || density != -1 || futureH != -1)
                    {
                        error = density != -1 ? SourceSetParseError.DescriptorDensityMoreThanOnce : SourceSetParseError.DescriptorMixWidthDensity;
                    }
                    else if (PositiveFloat().IsMatch(value) && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
                    {
                        density = parsed;
                    }
                    else
                    {
                        error = SourceSetParseError.DescriptorDensityNotFloat;
                    }
                }
                else if (last == 'h')
                {
                    if (futureH != -1 || density != -1)
                    {
                        error = futureH != -1 ? SourceSetParseError.DescriptorFutureHMoreThanOnce : SourceSetParseError.DescriptorFutureHWithDensity;
                    }
                    else if (TryParseInt(value, out int parsedHeight))
                    {
                        futureH = parsedHeight;
                        if (futureH == 0)
                        {
                            error = SourceSetParseError.DescriptorFutureHZero;
                            futureH = -1;
                        }
                    }
                    else
                    {
                        error = SourceSetParseError.DescriptorFutureHNotInt;
                    }
                }
                else
                {
                    error = SourceSetParseError.DescriptorUnknownSuffix;
                }
            }

            if (futureH != -1 && width == -1)
            {
                error = SourceSetParseError.DescriptorFutureHWithoutWidth;
            }

            string source = url.ToString();
            url.Clear();
            descriptors.Clear();
            if (error is { } code)
            {
                Error(code, -1);
                return null;
            }

            return new ImageSource(source, width < 0 ? null : width, density < 0 ? null : density);
        }

        // Java's Integer.parseInt, for a value without a sign (a signed descriptor was rejected before): ASCII digits in the range of an int.
        private static bool TryParseInt(string value, out int result)
        {
            result = 0;
            return value.Length > 0 && value.All(ch => ch is >= '0' and <= '9')
                && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result);
        }

        private void Error(SourceSetParseError code, int position) => errorHandler?.Invoke(code, position);
    }
}
