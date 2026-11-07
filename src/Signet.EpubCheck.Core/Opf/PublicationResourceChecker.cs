using System;
using Signet.EpubCheck.Core.Css;
using Signet.EpubCheck.Core.Dtbook;
using Signet.EpubCheck.Core.Media;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Ops;
using Signet.EpubCheck.Core.Overlay;
using Signet.EpubCheck.Core.Util.Mime;

namespace Signet.EpubCheck.Core.Opf;

/// <summary>Checks a resource (upstream's <c>Checker</c>).</summary>
public interface IChecker
{
    /// <summary>Checks the resource.</summary>
    void Check();
}

/// <summary>
/// The checker of a resource of a publication (upstream's <c>PublicationResourceChecker</c>): in a container, the resource
/// must exist (<c>RSC-001</c>) and be readable (<c>RSC-004</c>) before its content is checked. It is also the checker of the
/// resources that have no check of their content.
/// </summary>
public class PublicationResourceChecker : IChecker
{
    /// <summary>A checker of the resource of <paramref name="context"/>.</summary>
    public PublicationResourceChecker(ValidationContext context)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>The context of the check.</summary>
    protected ValidationContext Context { get; }

    /// <summary>The report.</summary>
    protected Reporting.IReport Report => Context.Report;

    /// <inheritdoc />
    public void Check()
    {
        bool inContainer = Context.Container is not null;
        bool proceed = !inContainer || CheckPublicationBeforeContent();
        proceed = proceed && CheckContent();

        // Upstream writes "cont && !container.isPresent() || checkPublicationAfterContent()": in a container the checks after
        // the content run even when the resource was not found or could not be read.
        _ = (proceed && !inContainer) || CheckPublicationAfterContent();
    }

    /// <summary>The checks before the content: the resource exists and can be decrypted.</summary>
    protected virtual bool CheckPublicationBeforeContent() => CheckResourceExists() && CheckResourceCanBeDecrypted();

    /// <summary>The check of the content of the resource.</summary>
    protected virtual bool CheckContent() => true;

    /// <summary>The checks that need the content to be read.</summary>
    protected virtual bool CheckPublicationAfterContent() => true;

    private bool CheckResourceExists()
    {
        if (!Context.Container!.Contains(Context.Url))
        {
            Report.Message(MessageId.RSC_001, Context.Location(), Context.Path);
            return false;
        }

        return true;
    }

    private bool CheckResourceCanBeDecrypted()
    {
        if (!Context.Container!.CanDecrypt(Context.Url))
        {
            Report.Message(MessageId.RSC_004, Context.Location(), Context.Path);
            return false;
        }

        return true;
    }
}

/// <summary>Picks the checker of a resource by its media type (upstream's <c>CheckerFactory</c>).</summary>
public static class CheckerFactory
{
    /// <summary>The checker of the resource of <paramref name="context"/>.</summary>
    public static IChecker NewChecker(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        string mimeType = (context.MimeType ?? string.Empty).ToLowerInvariant();
        switch (mimeType)
        {
            case MediaTypes.Css:
                return new CSSChecker(context);
            case MediaTypes.DtBook:
                return new DTBookChecker(context);
            case MediaTypes.Html or MediaTypes.Oebps when context.Version == EpubVersion.Version2:
                return new OPSChecker(context);
            case MediaTypes.ImageGif or MediaTypes.ImageJpeg or MediaTypes.ImagePng:
                return new BitmapChecker(context);
            case MediaTypes.Smil when context.Version == EpubVersion.Version3:
                return new OverlayChecker(context);
            case MediaTypes.PackageDocument:
                return context.Version == EpubVersion.Version2 ? new OPFChecker(context) : new OPFChecker30(context);
            case MediaTypes.Svg or MediaTypes.Xhtml:
                return new OPSChecker(context);
        }

        // The checker of search key maps joins with the task that ports it.
        return new PublicationResourceChecker(context);
    }
}
