using System;
using System.Collections.Generic;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Opf;
using Signet.EpubCheck.Core.Vocab;
using Signet.EpubCheck.Core.Xml;

namespace Signet.EpubCheck.Core.Ocf;

/// <summary>
/// Reads <c>META-INF/container.xml</c> for what decides the rest of the check (upstream's <c>OCFContainerFileChecker</c>
/// and <c>OCFContainerFileHandler</c>): the package documents (<c>rootfile</c>) and the mapping document of multiple
/// renditions (<c>link</c>). It does not validate the file: <see cref="OCFMetaFileChecker"/> does, once the version is known.
/// </summary>
internal static class OCFContainerFileChecker
{
    public static void Check(ValidationContext context, OCFCheckerState state)
    {
        XmlParser parser = new(context);
        parser.AddContentHandler(new Handler(context, state));
        parser.Process();
    }

    private sealed class Handler(ValidationContext context, OCFCheckerState state) : XmlHandler(context, state.Container.RootUrl)
    {
        private const string UnknownMediaType = "unknown";
        private const string MappingRelation = "mapping";

        protected override void StartElement()
        {
            if (CurrentElement.Namespace == Namespaces.OcfContainer)
            {
                switch (CurrentElement.Name)
                {
                    case "rootfile":
                        ProcessRootFile();
                        break;
                    case "link":
                        ProcessMappingDocument();
                        break;
                }
            }
        }

        private void ProcessRootFile()
        {
            string? fullPath = CurrentElement.GetAttribute("full-path");
            string mediaType = (CurrentElement.GetAttribute("media-type") ?? UnknownMediaType).Trim();
            if (fullPath is null)
            {
                Report.Message(MessageId.OPF_016, Location());
                return;
            }

            if (fullPath.Trim().Length == 0)
            {
                Report.Message(MessageId.OPF_017, Location());
                return;
            }

            if (CheckUrl(fullPath) is { } url)
            {
                state.AddRootfile(mediaType, url);
            }
        }

        private void ProcessMappingDocument()
        {
            string? href = CurrentElement.GetAttribute("href");
            if ((CurrentElement.GetAttribute("rel") ?? string.Empty).Trim() == MappingRelation && (href ?? string.Empty).Trim().Length > 0
                && CheckUrl(href) is { } url)
            {
                state.AddMappingDocument(url);
            }
        }
    }
}

/// <summary>
/// Validates <c>META-INF/encryption.xml</c> and notes the encrypted and obfuscated resources (upstream's
/// <c>OCFEncryptionFileChecker</c> and <c>OCFEncryptionFileHandler</c>).
/// </summary>
internal static class OCFEncryptionFileChecker
{
    public static void Check(ValidationContext context, OCFCheckerState state)
    {
        XmlParser parser = new(context);
        parser.AddContentHandler(new Handler(context, state));
        foreach (XmlValidators validator in context.Version switch
        {
            EpubVersion.Version3 => [XmlValidators.Enc30Rnc, XmlValidators.Enc30Sch],
            EpubVersion.Version2 => [XmlValidators.Enc20Rng],
            _ => Array.Empty<XmlValidators>(),
        })
        {
            parser.AddValidator(validator.Get());
        }

        parser.Process();
    }

    private sealed class Handler(ValidationContext context, OCFCheckerState state) : XmlHandler(context, state.Container.RootUrl)
    {
        private const string AlgorithmKey = "algorithm";
        private const string IdpfObfuscation = "http://www.idpf.org/2008/embedding";
        private const string AdobeObfuscation = "http://ns.adobe.com/pdf/enc#RC";
        private const string AdobeCompressionNamespace = "http://ns.adobe.com/digitaleditions/enc";
        private const string FontObfuscationArgument = "font-obfuscation";

        protected override void StartElement()
        {
            XMLElement element = CurrentElement;
            if (element.Name == "CipherReference")
            {
                ProcessCipherReference(element);
            }
            else if (element.Name == "EncryptionMethod" && element.GetAttribute("Algorithm") is { } algorithm
                && element.Parent is { } parent && parent.GetAttributeNS(AdobeCompressionNamespace, "compression") is null)
            {
                parent.SetPrivateData(AlgorithmKey, algorithm);
            }
        }

        private void ProcessCipherReference(XMLElement element)
        {
            string? algorithm = null;
            if (element.Parent?.Parent is { Name: "EncryptedData" } data)
            {
                algorithm = (string?)data.GetPrivateData(AlgorithmKey);
            }

            string? uri = element.GetAttribute("URI");
            if (CheckUrl(uri) is not { } url)
            {
                return;
            }

            if (!state.Container.Contains(url))
            {
                Report.Message(MessageId.RSC_007, Location(), uri);
                return;
            }

            switch (algorithm ?? string.Empty)
            {
                case IdpfObfuscation:
                    state.AddEncryptedResource(url, EncryptionKind.IdpfFontObfuscation);
                    state.AddObfuscatedResource(url, Location());
                    Report.Message(MessageId.OBS_001, Location(), FontObfuscationArgument);
                    break;
                case AdobeObfuscation:
                    state.AddEncryptedResource(url, EncryptionKind.AdobeFontObfuscation);
                    break;
                default:
                    state.AddEncryptedResource(url, EncryptionKind.Unsupported);
                    break;
            }
        }
    }
}

/// <summary>
/// Validates the other files of <c>META-INF</c> with their schemas (upstream's <c>OCFMetaFileChecker</c>): the container
/// file again (the first reading only looked for the rootfiles), <c>metadata.xml</c> of multiple renditions, and
/// <c>signatures.xml</c>.
/// </summary>
internal static class OCFMetaFileChecker
{
    public static void Check(ValidationContext context)
    {
        XmlParser parser = new(context);
        foreach (XmlValidators validator in ValidatorsFor(context))
        {
            parser.AddValidator(validator.Get());
        }

        parser.Process();
    }

    private static List<XmlValidators> ValidatorsFor(ValidationContext context)
    {
        List<XmlValidators> validators = [];
        string path = context.Path;
        if (path == OCFMetaFile.Container.AsPath())
        {
            validators.AddRange(context.Version switch
            {
                EpubVersion.Version2 => [XmlValidators.Container20Rng],
                EpubVersion.Version3 => [XmlValidators.Container30Rnc, XmlValidators.Container30RenditionsSch],
                _ => Array.Empty<XmlValidators>(),
            });
        }
        else if (path == OCFMetaFile.Signatures.AsPath())
        {
            validators.AddRange(context.Version switch
            {
                EpubVersion.Version2 => [XmlValidators.Sig20Rng],
                EpubVersion.Version3 => [XmlValidators.Sig30Rnc],
                _ => Array.Empty<XmlValidators>(),
            });
        }
        else if (path == OCFMetaFile.Metadata.AsPath() && context.HasProperty(EpubCheckVocab.Property.MultipleRendition))
        {
            validators.AddRange([XmlValidators.Meta30Rnc, XmlValidators.Meta30Sch]);
            if (context.Profile == EpubProfile.Edupub)
            {
                validators.Add(XmlValidators.MetaEdupubSch);
            }
        }

        return validators;
    }
}
