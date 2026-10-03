using System.Linq;
using System.Text.RegularExpressions;
using Signet.Core.Localization;

namespace Signet.Core.Tests.TestSupport;

/// <summary>
/// Texts from <see cref="CoreStrings"/> for assertions independent of the UI language (the tests run
/// in the environment culture — the neutral Polish or English).
/// </summary>
public static class LocalizedText
{
    private static readonly Regex Placeholder = new(@"\{\d+(?:[,:][^}]*)?\}", RegexOptions.Compiled);

    /// <summary>The longest constant fragment of the template (without the <c>{n}</c> arguments) — for <c>Contains</c>.</summary>
    public static string Fragment(string key) =>
        Placeholder.Split(CoreStrings.Get(key)).OrderByDescending(p => p.Trim().Length).First().Trim();
}
