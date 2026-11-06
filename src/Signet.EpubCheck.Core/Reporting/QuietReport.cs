namespace Signet.EpubCheck.Core.Reporting;

/// <summary>A report that discards everything (upstream's <c>QuietReport</c>): for parsing that must not tell anything.</summary>
public sealed class QuietReport : ReportBase
{
    private QuietReport()
    {
    }

    /// <summary>The report.</summary>
    public static QuietReport Instance { get; } = new();

    /// <inheritdoc />
    public override void Info(string? resource, FeatureKind feature, string? value)
    {
    }

    /// <inheritdoc />
    protected override void OnMessage(Messages.Message message, EpubLocation location, object?[] args)
    {
    }
}
