using System;
using System.Threading;

namespace Signet.EpubCheck.Core.Xml;

/// <summary>The schemas EPUBCheck validates with (upstream's <c>XMLValidators</c>), in upstream's order.</summary>
public enum XmlValidators
{
    /// <summary><c>schema/20/rng/container.rng</c>.</summary>
    Container20Rng,

    /// <summary><c>schema/30/ocf-container-30.rnc</c>.</summary>
    Container30Rnc,

    /// <summary><c>schema/30/multiple-renditions/container.sch</c>.</summary>
    Container30RenditionsSch,

    /// <summary><c>schema/20/rng/dtbook-2005-2.rng</c>.</summary>
    DtbookRng,

    /// <summary><c>schema/20/rng/encryption.rng</c>.</summary>
    Enc20Rng,

    /// <summary><c>schema/30/ocf-encryption-30.rnc</c>.</summary>
    Enc30Rnc,

    /// <summary><c>schema/30/ocf-encryption-30.sch</c>.</summary>
    Enc30Sch,

    /// <summary><c>schema/20/sch/id-unique.sch</c>.</summary>
    Idunique20Sch,

    /// <summary><c>schema/30/ocf-metadata-30.rnc</c>.</summary>
    Meta30Rnc,

    /// <summary><c>schema/30/ocf-metadata-30.sch</c>.</summary>
    Meta30Sch,

    /// <summary><c>schema/30/edupub/edu-ocf-metadata.sch</c>.</summary>
    MetaEdupubSch,

    /// <summary><c>schema/30/media-overlay-30.rnc</c>.</summary>
    Mo30Rnc,

    /// <summary><c>schema/30/media-overlay-30.sch</c>.</summary>
    Mo30Sch,

    /// <summary><c>schema/30/epub-nav-30.rnc</c>.</summary>
    Nav30Rnc,

    /// <summary><c>schema/30/epub-nav-30.sch</c>.</summary>
    Nav30Sch,

    /// <summary><c>schema/20/rng/ncx.rng</c>.</summary>
    NcxRng,

    /// <summary><c>schema/20/sch/ncx.sch</c>.</summary>
    NcxSch,

    /// <summary><c>schema/20/rng/opf.rng</c>.</summary>
    Opf20Rng,

    /// <summary><c>schema/20/sch/opf.sch</c>.</summary>
    Opf20Sch,

    /// <summary><c>schema/30/package-30.rnc</c>.</summary>
    Opf30Rnc,

    /// <summary><c>schema/30/package-30.sch</c>.</summary>
    Opf30Sch,

    /// <summary><c>schema/30/dict/dict-collection.sch</c>.</summary>
    Opf30CollectionDictSch,

    /// <summary><c>schema/30/collection-do-30.sch</c>.</summary>
    Opf30CollectionDoSch,

    /// <summary><c>schema/30/idx/idx-collection.sch</c>.</summary>
    Opf30CollectionIdxSch,

    /// <summary><c>schema/30/collection-manifest-30.sch</c>.</summary>
    Opf30CollectionManifestSch,

    /// <summary><c>schema/30/previews/preview-collection.sch</c>.</summary>
    Opf30CollectionPreviewSch,

    /// <summary><c>schema/30/dict/dict-opf.sch</c>.</summary>
    OpfDictSch,

    /// <summary><c>schema/30/edupub/edu-opf.sch</c>.</summary>
    OpfEdupubSch,

    /// <summary><c>schema/30/previews/preview-pub-opf.sch</c>.</summary>
    OpfPreviewSch,

    /// <summary><c>schema/30/multiple-renditions/mapping.rnc</c>.</summary>
    RenditionMappingRnc,

    /// <summary><c>schema/30/multiple-renditions/mapping.sch</c>.</summary>
    RenditionMappingSch,

    /// <summary><c>schema/30/dict/search-key-map.rnc</c>.</summary>
    SearchKeyMapRnc,

    /// <summary><c>schema/20/rng/signatures.rng</c>.</summary>
    Sig20Rng,

    /// <summary><c>schema/30/ocf-signatures-30.rnc</c>.</summary>
    Sig30Rnc,

    /// <summary><c>schema/20/rng/ops20-svg.nvdl</c>.</summary>
    Svg20Nvdl,

    /// <summary><c>schema/30/epub-svg-30.rnc</c>.</summary>
    Svg30Rnc,

    /// <summary><c>schema/30/epub-svg-30.nvdl</c>.</summary>
    Svg30Nvdl,

    /// <summary><c>schema/30/epub-svg-30-informative.nvdl</c> (informative).</summary>
    Svg30InformativeNvdl,

    /// <summary><c>schema/30/epub-svg-30.sch</c>.</summary>
    Svg30Sch,

    /// <summary><c>schema/20/rng/ops20.nvdl</c>.</summary>
    Xhtml20Nvdl,

    /// <summary><c>schema/20/sch/xhtml.sch</c>.</summary>
    Xhtml20Sch,

    /// <summary><c>schema/30/epub-xhtml-30.sch</c>.</summary>
    Xhtml30Sch,

    /// <summary><c>schema/30/epub-xhtml-30.rnc</c>.</summary>
    Xhtml30Rnc,

    /// <summary><c>schema/30/epub-xhtml-30.nvdl</c>.</summary>
    Xhtml30Nvdl,

    /// <summary><c>schema/30/edupub/edu-structure.sch</c>.</summary>
    XhtmlEdupubStructureSch,

    /// <summary><c>schema/30/edupub/edu-semantics.sch</c>.</summary>
    XhtmlEdupubSemanticsSch,

    /// <summary><c>schema/30/datanav/datanav-xhtml.sch</c>.</summary>
    XhtmlDatanavSch,

    /// <summary><c>schema/30/dict/dict-xhtml.sch</c>.</summary>
    XhtmlDictSch,

    /// <summary><c>schema/30/idx/idx-xhtml.sch</c>.</summary>
    XhtmlIdxSch,

    /// <summary><c>schema/30/idx/idx-xhtml-index.sch</c>.</summary>
    XhtmlIdxIndexSch
}

/// <summary>
/// The compiled schema of each <see cref="XmlValidators"/>. Upstream's enum reads all its schemas when it is first used;
/// the port reads each one the first time it is asked for, once (from any thread), and keeps it for the life of the
/// process.
/// </summary>
public static class XmlValidatorsExtensions
{
    private static readonly Lazy<XmlValidator>[] s_validators = Array.ConvertAll(
        Enum.GetValues<XmlValidators>(),
        v => new Lazy<XmlValidator>(() => Create(v), LazyThreadSafetyMode.ExecutionAndPublication));

    /// <summary>The compiled schema of <paramref name="validator"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="validator"/> is not a defined value.</exception>
    public static XmlValidator Get(this XmlValidators validator) =>
        (uint)validator < (uint)s_validators.Length
            ? s_validators[(int)validator].Value
            : throw new ArgumentOutOfRangeException(nameof(validator), validator, null);

    private static XmlValidator Create(XmlValidators validator) => validator switch
    {
        XmlValidators.Container20Rng => new("schema/20/rng/container.rng"),
        XmlValidators.Container30Rnc => new("schema/30/ocf-container-30.rnc"),
        XmlValidators.Container30RenditionsSch => new("schema/30/multiple-renditions/container.sch"),
        XmlValidators.DtbookRng => new("schema/20/rng/dtbook-2005-2.rng"),
        XmlValidators.Enc20Rng => new("schema/20/rng/encryption.rng"),
        XmlValidators.Enc30Rnc => new("schema/30/ocf-encryption-30.rnc"),
        XmlValidators.Enc30Sch => new("schema/30/ocf-encryption-30.sch"),
        XmlValidators.Idunique20Sch => new("schema/20/sch/id-unique.sch"),
        XmlValidators.Meta30Rnc => new("schema/30/ocf-metadata-30.rnc"),
        XmlValidators.Meta30Sch => new("schema/30/ocf-metadata-30.sch"),
        XmlValidators.MetaEdupubSch => new("schema/30/edupub/edu-ocf-metadata.sch"),
        XmlValidators.Mo30Rnc => new("schema/30/media-overlay-30.rnc"),
        XmlValidators.Mo30Sch => new("schema/30/media-overlay-30.sch"),
        XmlValidators.Nav30Rnc => new("schema/30/epub-nav-30.rnc"),
        XmlValidators.Nav30Sch => new("schema/30/epub-nav-30.sch"),
        XmlValidators.NcxRng => new("schema/20/rng/ncx.rng"),
        XmlValidators.NcxSch => new("schema/20/sch/ncx.sch"),
        XmlValidators.Opf20Rng => new("schema/20/rng/opf.rng"),
        XmlValidators.Opf20Sch => new("schema/20/sch/opf.sch"),
        XmlValidators.Opf30Rnc => new("schema/30/package-30.rnc"),
        XmlValidators.Opf30Sch => new("schema/30/package-30.sch"),
        XmlValidators.Opf30CollectionDictSch => new("schema/30/dict/dict-collection.sch"),
        XmlValidators.Opf30CollectionDoSch => new("schema/30/collection-do-30.sch"),
        XmlValidators.Opf30CollectionIdxSch => new("schema/30/idx/idx-collection.sch"),
        XmlValidators.Opf30CollectionManifestSch => new("schema/30/collection-manifest-30.sch"),
        XmlValidators.Opf30CollectionPreviewSch => new("schema/30/previews/preview-collection.sch"),
        XmlValidators.OpfDictSch => new("schema/30/dict/dict-opf.sch"),
        XmlValidators.OpfEdupubSch => new("schema/30/edupub/edu-opf.sch"),
        XmlValidators.OpfPreviewSch => new("schema/30/previews/preview-pub-opf.sch"),
        XmlValidators.RenditionMappingRnc => new("schema/30/multiple-renditions/mapping.rnc"),
        XmlValidators.RenditionMappingSch => new("schema/30/multiple-renditions/mapping.sch"),
        XmlValidators.SearchKeyMapRnc => new("schema/30/dict/search-key-map.rnc"),
        XmlValidators.Sig20Rng => new("schema/20/rng/signatures.rng"),
        XmlValidators.Sig30Rnc => new("schema/30/ocf-signatures-30.rnc"),
        XmlValidators.Svg20Nvdl => new("schema/20/rng/ops20-svg.nvdl"),
        XmlValidators.Svg30Rnc => new("schema/30/epub-svg-30.rnc"),
        XmlValidators.Svg30Nvdl => new("schema/30/epub-svg-30.nvdl"),
        XmlValidators.Svg30InformativeNvdl => new("schema/30/epub-svg-30-informative.nvdl", isNormative: false),
        XmlValidators.Svg30Sch => new("schema/30/epub-svg-30.sch"),
        XmlValidators.Xhtml20Nvdl => new("schema/20/rng/ops20.nvdl"),
        XmlValidators.Xhtml20Sch => new("schema/20/sch/xhtml.sch"),
        XmlValidators.Xhtml30Sch => new("schema/30/epub-xhtml-30.sch"),
        XmlValidators.Xhtml30Rnc => new("schema/30/epub-xhtml-30.rnc"),
        XmlValidators.Xhtml30Nvdl => new("schema/30/epub-xhtml-30.nvdl"),
        XmlValidators.XhtmlEdupubStructureSch => new("schema/30/edupub/edu-structure.sch"),
        XmlValidators.XhtmlEdupubSemanticsSch => new("schema/30/edupub/edu-semantics.sch"),
        XmlValidators.XhtmlDatanavSch => new("schema/30/datanav/datanav-xhtml.sch"),
        XmlValidators.XhtmlDictSch => new("schema/30/dict/dict-xhtml.sch"),
        XmlValidators.XhtmlIdxSch => new("schema/30/idx/idx-xhtml.sch"),
        XmlValidators.XhtmlIdxIndexSch => new("schema/30/idx/idx-xhtml-index.sch"),
        _ => throw new ArgumentOutOfRangeException(nameof(validator), validator, null),
    };
}
