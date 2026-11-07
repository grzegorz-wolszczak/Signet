using System;
using System.Collections.Generic;
using System.Globalization;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Nav;
using Signet.EpubCheck.Core.Ncx;
using Signet.EpubCheck.Core.Ocf;
using Signet.EpubCheck.Core.References;
using Signet.EpubCheck.Core.Reporting;
using Signet.EpubCheck.Core.Util;
using Signet.EpubCheck.Core.Vocab;
using Signet.EpubCheck.Core.Xml;

namespace Signet.EpubCheck.Core.Opf;

/// <summary>
/// Checks a package document (upstream's <c>OPFChecker</c>, which is the checker of EPUB 2): the document is validated
/// with the schemas of its version, read by <see cref="OPFHandler"/>, then its manifest items (fallbacks, media types),
/// spine and guide are checked. The checks of the resources the manifest declares (content documents, the NCX, the
/// resource references) come with the tasks that own those resources.
/// </summary>
public class OPFChecker(ValidationContext context) : IChecker
{
    private const string PackageMimeType = "opf";

    /// <summary>The context of the check.</summary>
    protected ValidationContext Context { get; } = context ?? throw new ArgumentNullException(nameof(context));

    /// <summary>The report.</summary>
    protected IReport Report => Context.Report;

    /// <summary>The version checked against.</summary>
    protected EpubVersion Version => Context.Version;

    /// <summary>The handler that read the package document (<c>null</c> before <see cref="Check"/>).</summary>
    public OPFHandler? OpfHandler { get; protected set; }

    /// <summary>Checks the package document.</summary>
    public virtual void Check()
    {
        if (Context.Container is not null)
        {
            CheckPackage();
        }
        else
        {
            CheckContent();
        }
    }

    /// <summary>Makes the handler that reads the package document.</summary>
    protected virtual OPFHandler CreateHandler() => new(Context);

    /// <summary>The schemas the package document is validated with.</summary>
    protected virtual IEnumerable<XmlValidators> Validators()
    {
        if (Version == EpubVersion.Version2)
        {
            yield return XmlValidators.Opf20Rng;
            yield return XmlValidators.Opf20Sch;
        }
    }

    /// <summary>Checks the package document of a container: the document, the items and the guide.</summary>
    protected virtual bool CheckPackage()
    {
        OCFContainer container = Context.Container!;
        if (!container.Contains(Context.Url))
        {
            Report.Message(MessageId.PKG_020, Context.Location(), Context.Path);
            return false;
        }

        CheckContent();
        OPFHandler handler = OpfHandler!;
        if (!handler.CheckUniqueIdentExists())
        {
            Report.Message(MessageId.OPF_030, Context.Location(), handler.IdentId);
        }

        IReadOnlyList<OPFItem> items = handler.Items;
        Report.Info(null, FeatureKind.ItemsCount, items.Count.ToString(CultureInfo.InvariantCulture));
        Context.ResourceRegistry!.RegisterResource(Context.Url, Context.MimeType);
        foreach (OPFItem item in items)
        {
            Context.ResourceRegistry.RegisterResource(item);
            Report.Info(item.Path, FeatureKind.DeclaredMimetype, item.MimeType);
        }

        CheckGuide();
        foreach (OPFItem item in items)
        {
            if (!item.IsRemote)
            {
                CheckItemContent(item);
            }
        }

        foreach (OPFItem item in items)
        {
            CheckItemAfterResourceValidation(item);
        }

        new ResourceReferencesChecker(Context).Check();
        return false;
    }

    /// <summary>
    /// Checks the resource an item declares with the checker of its kind: the NCX, the navigation document or the one
    /// <see cref="CheckerFactory"/> picks. A checker that cannot be made for the item (its media type is not the one the kind
    /// needs) is <c>CHK-008</c>.
    /// </summary>
    protected virtual void CheckItemContent(OPFItem item)
    {
        if (item.HasDataUrl || EpubCheckUrlUtils.DocUrl(item.Url).Equals(Context.Url))
        {
            return;
        }

        ValidationContext itemContext = Context with
        {
            Url = item.Url,
            MimeType = item.MimeType,
            Properties = new HashSet<VocabProperty>(item.Properties),
        };
        try
        {
            IChecker checker = item.IsNcx ? new NCXChecker(itemContext) : item.IsNav ? new NavChecker(itemContext) : CheckerFactory.NewChecker(itemContext);
            checker.Check();
        }
        catch (InvalidOperationException)
        {
            Report.Message(MessageId.CHK_008, Context.Location(), item.Path);
        }
    }

    /// <summary>Checks an item once the resources were validated.</summary>
    protected virtual void CheckItemAfterResourceValidation(OPFItem item)
    {
    }

    /// <summary>Checks that the guide points to items that are content documents.</summary>
    protected virtual void CheckGuide()
    {
        OPFHandler handler = OpfHandler!;
        for (int i = 0; i < handler.ReferenceCount; i++)
        {
            OPFReference reference = handler.GetReference(i);

            // Upstream fails on a reference whose URL did not parse (already reported as RSC-020).
            if (reference.Url is null)
            {
                continue;
            }

            EpubLocation location = Context.Location().At(reference.LineNumber, reference.ColumnNumber);
            OPFItem? item = handler.GetItemByUrl(reference.DocumentUrl!);
            if (item is null)
            {
                Report.Message(MessageId.OPF_031, location, Context.Relativize(reference.Url));
            }
            else if (!OpfMediaTypes.IsBlessedItemType(item.MimeType, Version) && !OpfMediaTypes.IsDeprecatedBlessedItemType(item.MimeType))
            {
                Report.Message(MessageId.OPF_032, location, Context.Relativize(reference.Url));
            }
        }
    }

    /// <summary>Validates and reads the package document, and checks its items and spine.</summary>
    protected virtual bool CheckContent()
    {
        XmlParser parser = new(Context with { MimeType = PackageMimeType });
        OPFHandler handler = CreateHandler();
        OpfHandler = handler;
        foreach (XmlValidators validator in Validators())
        {
            parser.AddValidator(validator.Get());
        }

        parser.AddContentHandler(handler);
        parser.Process();

        foreach (OPFItem item in handler.Items)
        {
            if (Context.Container is null && !item.IsRemote && !item.HasDataUrl)
            {
                OCFFilenameChecker.Check(item.Path, Version, Report, item.Location);
            }

            if (!item.Equals(handler.GetItemByUrl(item.Url)))
            {
                Report.Message(MessageId.OPF_074, item.Location, item.Path);
            }
            else if (item.Url.Equals(Context.Url))
            {
                Report.Message(MessageId.OPF_099, item.Location);
            }
            else
            {
                CheckItem(item, handler);
            }
        }

        CheckSpine(handler);
        return true;
    }

    /// <summary>Checks an item of the manifest.</summary>
    protected virtual void CheckItem(OPFItem item, OPFHandler handler)
    {
        string mimeType = item.MimeType;
        EpubLocation location = item.Location.WithContext(item.Id);
        if (OpfMediaTypes.IsWellFormed(mimeType)
            && (OpfMediaTypes.IsDeprecatedBlessedItemType(mimeType) || OpfMediaTypes.IsDeprecatedBlessedStyleType(mimeType)))
        {
            if (handler.IsOpf20PackageFile && mimeType == "text/html")
            {
                Report.Message(MessageId.OPF_035, location);
            }
            else if (handler.IsOpf12PackageFile && mimeType == "text/html")
            {
                Report.Message(MessageId.OPF_038, location, mimeType);
            }
            else if (handler.IsOpf20PackageFile)
            {
                Report.Message(MessageId.OPF_037, location, mimeType);
            }
        }

        if (handler.IsOpf12PackageFile && !item.HasFallback)
        {
            if (OpfMediaTypes.IsBlessedItemType(mimeType, Version))
            {
                Report.Message(MessageId.OPF_038, location, mimeType);
            }
            else if (OpfMediaTypes.IsBlessedStyleType(mimeType))
            {
                Report.Message(MessageId.OPF_039, location, mimeType);
            }
        }
    }

    /// <summary>Checks an item of the spine.</summary>
    protected virtual void CheckSpineItem(OPFItem item, OPFHandler handler)
    {
        string mimeType = item.MimeType;
        if (OpfMediaTypes.IsBlessedStyleType(mimeType) || OpfMediaTypes.IsDeprecatedBlessedStyleType(mimeType)
            || OpfMediaTypes.IsBlessedImageType(mimeType, Version))
        {
            Report.Message(MessageId.OPF_042, item.Location, mimeType);
        }
        else if (!OpfMediaTypes.IsBlessedItemType(mimeType, Version) && !OpfMediaTypes.IsDeprecatedBlessedItemType(mimeType))
        {
            if (!item.HasFallback)
            {
                Report.Message(MessageId.OPF_043, item.Location, mimeType);
            }
            else if (!item.HasContentDocumentFallback)
            {
                Report.Message(MessageId.OPF_044, item.Location, mimeType);
            }
        }
    }

    private void CheckSpine(OPFHandler handler)
    {
        if (handler.SpineItems.Count > 0)
        {
            bool linearFound = false;
            int spineIndex = 0;
            foreach (OPFItem item in handler.SpineItems)
            {
                CheckSpineItem(item, handler);
                linearFound |= item.IsLinear;
                Report.Info(item.Path, FeatureKind.SpineIndex, (spineIndex++).ToString(CultureInfo.InvariantCulture));
            }

            if (!linearFound)
            {
                Report.Message(MessageId.OPF_033, Context.Location());
            }
        }

        if (Version == EpubVersion.Version2)
        {
            HashSet<OPFItem> seen = [];
            foreach (OPFItem item in handler.SpineItems)
            {
                if (!seen.Add(item))
                {
                    Report.Message(MessageId.OPF_034, item.Location, item.Id);
                }
            }
        }
    }
}
