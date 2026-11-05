namespace Signet.EpubCheck.Core.Reporting;

public static partial class FeatureKinds
{
    /// <summary>The text form of the feature, as the reports print it (<c>"audio element"</c>, <c>"language"</c>, …).</summary>
    public static string ToText(this FeatureKind feature) => s_texts[(int)feature];
}
