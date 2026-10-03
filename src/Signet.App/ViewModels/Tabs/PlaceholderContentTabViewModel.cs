using Signet.App.Resources;
using Signet.Core.MainUI;
using Signet.Core.Resources;

namespace Signet.App.ViewModels.Tabs;

/// <summary>
/// Placeholder tab: shows the resource path, the kind of the target tab and — for text
/// resources — their content in read-only mode. Used only for unrecognized resources
/// (<see cref="ContentTabKind.Unsupported"/>), and those are blocked before a tab is opened anyway.
/// </summary>
public sealed class PlaceholderContentTabViewModel : ContentTabViewModel
{
    /// <summary>Maximum length of the text preview in the placeholder.</summary>
    private const int MaxPreviewChars = 20_000;

    /// <summary>Creates a placeholder tab for a model entry.</summary>
    public PlaceholderContentTabViewModel(OpenTab tab)
        : base(tab)
    {
        if (tab.Resource is TextResource text)
        {
            text.InitialLoad();
            string full = text.GetText();
            IsTextPreview = true;
            Content = full.Length > MaxPreviewChars
                ? full[..MaxPreviewChars] + "\n\n… " + Strings.Get("PlaceholderTab_Truncated")
                : full;
        }

        Summary = BuildSummary(tab);
    }

    /// <summary>Short description: the path + the target tab kind.</summary>
    public string Summary { get; }

    /// <summary>Content of a text resource (empty for binary resources).</summary>
    public string Content { get; } = string.Empty;

    /// <summary>Whether to show a text preview (the resource is textual).</summary>
    public bool IsTextPreview { get; }

    private static string BuildSummary(OpenTab tab)
    {
        return Strings.Format("PlaceholderTab_Summary", tab.Resource.BookPath);
    }
}
