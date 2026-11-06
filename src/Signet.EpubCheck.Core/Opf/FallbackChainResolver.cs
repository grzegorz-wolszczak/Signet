using System.Collections.Generic;
using System.Linq;
using Signet.EpubCheck.Core.Messages;

namespace Signet.EpubCheck.Core.Opf;

/// <summary>
/// Follows the fallbacks of the manifest items (upstream's <c>FallbackChainResolver</c>): an item gets a content document
/// or core media type fallback from the item it falls back to, an unresolved chain is a cycle (<c>OPF-045</c>), a fallback
/// that does not exist is <c>OPF-040</c>, a missing <c>fallback-style</c> <c>OPF-041</c>.
/// </summary>
internal sealed class FallbackChainResolver(IReadOnlyDictionary<string, OPFItemBuilder> items, ValidationContext context)
{
    public List<OPFItem> Resolve()
    {
        LinkedList<OPFItemBuilder> queue = new(items.Values);
        List<OPFItemBuilder> resolved = new(queue.Count);
        int pending = 0; // items put back in the queue since the last one resolved
        while (queue.Count > 0 && pending <= queue.Count)
        {
            OPFItemBuilder item = queue.First!.Value;
            queue.RemoveFirst();
            string mimeType = item.MimeType;
            item.HasCoreMediaTypeFallback = item.HasCoreMediaTypeFallback
                || (context.Version == EpubVersion.Version2 && OpfMediaTypes.IsBlessedImageType(mimeType, context.Version))
                || OpfMediaTypes.IsCoreMediaType(mimeType);
            item.HasContentDocumentFallback = item.HasContentDocumentFallback
                || OpfMediaTypes.IsBlessedItemType(mimeType, context.Version)
                || OpfMediaTypes.IsDeprecatedBlessedItemType(mimeType);

            if (item.HasFallback)
            {
                if (!items.TryGetValue(item.Fallback, out OPFItemBuilder? fallback))
                {
                    context.Report.Message(MessageId.OPF_040, item.Location!, item.Fallback);
                }
                else if (fallback.IsResolved)
                {
                    if (item.FixedLayout)
                    {
                        fallback.FixedLayout = true;
                    }

                    item.HasContentDocumentFallback = item.HasContentDocumentFallback || fallback.HasContentDocumentFallback;
                    item.HasCoreMediaTypeFallback = item.HasCoreMediaTypeFallback || fallback.HasCoreMediaTypeFallback;
                }
                else
                {
                    queue.AddLast(item);
                    pending++;
                    continue;
                }
            }

            if (context.Version == EpubVersion.Version2 && item.HasFallbackStyle)
            {
                if (!items.TryGetValue(item.FallbackStyle, out OPFItemBuilder? fallbackStyle))
                {
                    // Upstream names the fallback, not the style, in this message.
                    context.Report.Message(MessageId.OPF_041, item.Location!, item.Fallback);
                }
                else
                {
                    bool valid = OpfMediaTypes.IsBlessedStyleType(fallbackStyle.MimeType) || OpfMediaTypes.IsDeprecatedBlessedStyleType(fallbackStyle.MimeType);
                    item.HasContentDocumentFallback = item.HasContentDocumentFallback || valid;
                    item.HasCoreMediaTypeFallback = item.HasCoreMediaTypeFallback || valid;
                }
            }

            resolved.Add(item);
            item.MarkResolved();
            pending = 0;
        }

        if (queue.Count > 0)
        {
            context.Report.Message(MessageId.OPF_045, queue.First!.Value.Location!);
            resolved.AddRange(queue);
        }

        return [.. resolved.Select(builder => builder.Build())];
    }
}
