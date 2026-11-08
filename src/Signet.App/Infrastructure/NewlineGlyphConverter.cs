using System;
using Avalonia.Data.Converters;

namespace Signet.App.Infrastructure;

/// <summary>Shows a text with line breaks on one line (a line break as "↵"), for a list of searches that may span lines.</summary>
public static class NewlineGlyphConverter
{
    /// <summary>Line breaks of the text become "↵".</summary>
    public static readonly IValueConverter Instance =
        new FuncValueConverter<string?, string>(value => (value ?? string.Empty).Replace("\n", "\u21B5", StringComparison.Ordinal));
}
