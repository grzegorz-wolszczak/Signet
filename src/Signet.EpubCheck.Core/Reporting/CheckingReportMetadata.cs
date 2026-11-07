using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Util;

namespace Signet.EpubCheck.Core.Reporting;

internal static class FeatureValues
{
    public static bool TryLong(string? value, out long number) =>
        long.TryParse(value?.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out number);

    public static bool IsTrue(string? value) => string.Equals(value?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
}

/// <summary>What the JSON report says about the checker (upstream's <c>CheckerMetadata</c>).</summary>
internal sealed class CheckerMetadata
{
    private const string DateFormat = "MM-dd-yyyy HH:mm:ss";
    private const long NotMeasured = -1;

    private string? _path;
    private string? _fileName;
    private string? _checkerVersion;
    private string? _checkDate;
    private long _elapsedTime = NotMeasured;
    private int _fatal;
    private int _error;
    private int _warning;
    private int _usage;
    private DateTime _start;
    private DateTime _end;

    public void SetFileInfo(string epubFile)
    {
        string full = Path.GetFullPath(epubFile);
        _path = PathUtil.RemoveWorkingDirectory(full);
        _fileName = Path.GetFileName(full);
    }

    public void SetCheckerVersion(string value) => _checkerVersion = value;

    public void SetStartDate()
    {
        _start = DateTime.Now;
        _checkDate = _start.ToString(DateFormat, CultureInfo.InvariantCulture);
    }

    public void SetStopDate()
    {
        _end = DateTime.Now;
        _elapsedTime = (long)(_end - _start).TotalMilliseconds;
    }

    public void SetMessageTypes(IEnumerable<CheckMessage> messages)
    {
        foreach (CheckMessage message in messages)
        {
            switch (message.Severity)
            {
                case Severity.Fatal: _fatal++; break;
                case Severity.Error: _error++; break;
                case Severity.Warning: _warning++; break;
                case Severity.Usage: _usage++; break;
            }
        }
    }

    public void Write(JacksonStyleJsonWriter json)
    {
        json.BeginObject();
        json.Name("path");
        json.String(_path);
        json.Name("filename");
        json.String(_fileName);
        json.Name("checkerVersion");
        json.String(_checkerVersion);
        json.Name("checkDate");
        json.String(_checkDate);
        json.Name("elapsedTime");
        json.Number(_elapsedTime);
        json.Name("nFatal");
        json.Number(_fatal);
        json.Name("nError");
        json.Number(_error);
        json.Name("nWarning");
        json.Number(_warning);
        json.Name("nUsage");
        json.Number(_usage);
        json.EndObject();
    }
}

/// <summary>What the JSON report says about one file of the publication (upstream's <c>ItemMetadata</c>).</summary>
internal sealed class ItemMetadata
{
    private readonly SortedSet<string> _referencedItems = new(StringComparer.Ordinal);
    private string? _mediaType;
    private long _compressedSize;
    private long _uncompressedSize;
    private string? _compressionMethod;
    private string? _checkSum;
    private int? _spineIndex;
    private bool _isLinear;
    private bool _isScripted;

    public ItemMetadata(string fileName)
    {
        FileName = fileName;
    }

    public string Id { get; set; } = string.Empty;

    public string FileName { get; }

    public string? RenditionLayout { get; set; }

    public string? RenditionOrientation { get; set; }

    public string? RenditionSpread { get; set; }

    public bool? IsFixedFormat { get; set; }

    public bool IsSpineItem { get; private set; }

    public void HandleInfo(FeatureKind feature, string? value)
    {
        switch (feature)
        {
            case FeatureKind.DeclaredMimetype:
                _mediaType = value;
                break;
            case FeatureKind.HasScripts:
                _isScripted = true;
                break;
            case FeatureKind.HasFixedLayout:
                IsFixedFormat = true;
                break;
            case FeatureKind.IsSpineitem:
                IsSpineItem = true;
                break;
            case FeatureKind.UniqueIdent:
                Id = value ?? string.Empty;
                break;
            case FeatureKind.IsLinear:
                _isLinear = FeatureValues.IsTrue(value);
                break;
            case FeatureKind.Resource:
                if (value is not null && value != FileName)
                {
                    _referencedItems.Add(value);
                }

                break;
            case FeatureKind.Size:
                if (FeatureValues.TryLong(value, out long size))
                {
                    _uncompressedSize = size;
                }

                break;
            case FeatureKind.CompressedSize:
                if (FeatureValues.TryLong(value, out long compressed))
                {
                    _compressedSize = compressed;
                }

                break;
            case FeatureKind.CompressionMethod:
                _compressionMethod = value;
                break;
            case FeatureKind.Sha256:
                _checkSum = value;
                break;
            case FeatureKind.SpineIndex:
                if (int.TryParse(value?.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int index))
                {
                    _spineIndex = index;
                }

                break;
            case FeatureKind.RenditionLayout:
                RenditionLayout = value;
                break;
            case FeatureKind.RenditionOrientation:
                RenditionOrientation = value;
                break;
            case FeatureKind.RenditionSpread:
                RenditionSpread = value;
                break;
        }
    }

    public void Write(JacksonStyleJsonWriter json)
    {
        json.BeginObject();
        json.Name("id");
        json.String(Id);
        json.Name("fileName");
        json.String(FileName);
        json.Name("media_type");
        json.String(_mediaType);
        json.Name("compressedSize");
        json.Number(_compressedSize);
        json.Name("uncompressedSize");
        json.Number(_uncompressedSize);
        json.Name("compressionMethod");
        json.String(_compressionMethod);
        json.Name("checkSum");
        json.String(_checkSum);
        json.Name("isSpineItem");
        json.Boolean(IsSpineItem);
        json.Name("spineIndex");
        if (_spineIndex is { } index)
        {
            json.Number(index);
        }
        else
        {
            json.Null();
        }

        json.Name("isLinear");
        json.Boolean(_isLinear);
        json.Name("isFixedFormat");
        if (IsFixedFormat is { } fixedFormat)
        {
            json.Boolean(fixedFormat);
        }
        else
        {
            json.Null();
        }

        json.Name("isScripted");
        json.Boolean(_isScripted);
        json.Name("renditionLayout");
        json.String(RenditionLayout);
        json.Name("renditionOrientation");
        json.String(RenditionOrientation);
        json.Name("renditionSpread");
        json.String(RenditionSpread);
        json.Name("referencedItems");
        json.StringArray(_referencedItems);
        json.EndObject();
    }
}

/// <summary>What the JSON report says about the publication (upstream's <c>PublicationMetadata</c>).</summary>
internal sealed class PublicationMetadata
{
    private const string Reflowable = "reflowable";
    private const string Auto = "auto";
    private const string FixedLayoutRendition = "pre-paginated";

    private readonly List<string> _creator = [];
    private readonly List<string> _subject = [];
    private readonly HashSet<string> _embeddedFonts = [];
    private readonly List<string> _embeddedFontOrder = [];
    private readonly HashSet<string> _refFonts = [];
    private readonly List<string> _refFontOrder = [];
    private readonly HashSet<string> _contributors = [];
    private readonly List<string> _contributorOrder = [];
    private string? _publisher;
    private string? _title;
    private string? _date;
    private string? _description;
    private string? _rights;
    private string? _identifier;
    private string? _language;
    private int _spines;
    private string? _epubVersion;
    private bool _isScripted;
    private bool _hasFixedFormat;
    private bool _isBackwardCompatible = true;
    private bool _hasAudio;
    private bool _hasVideo;
    private long _charsCount;
    private bool _hasEncryption;
    private bool _hasSignatures;

    public string RenditionLayout { get; private set; } = Reflowable;

    public string RenditionOrientation { get; private set; } = Auto;

    public string RenditionSpread { get; private set; } = Auto;

    public bool DefaultsToFixedFormat => RenditionLayout == FixedLayoutRendition;

    public void HandleInfo(string? resource, FeatureKind feature, string? value)
    {
        switch (feature)
        {
            case FeatureKind.DcTitle: _title = value; break;
            case FeatureKind.DcLanguage: _language = value; break;
            case FeatureKind.DcPublisher: _publisher = value; break;
            case FeatureKind.DcCreator: _creator.Add(value!); break;
            case FeatureKind.DcRights: _rights = value; break;
            case FeatureKind.DcSubject: _subject.Add(value!); break;
            case FeatureKind.DcDescription: _description = value; break;
            case FeatureKind.ModifiedDate: _date = value; break;
            case FeatureKind.UniqueIdent:
                if (resource is null)
                {
                    _identifier = value;
                }

                break;
            case FeatureKind.FormatVersion: _epubVersion = value; break;
            case FeatureKind.HasScripts:
                _isScripted = true;
                _isBackwardCompatible = false;
                break;
            case FeatureKind.HasFixedLayout:
                _hasFixedFormat = true;
                _isBackwardCompatible = false;
                break;
            case FeatureKind.IsSpineitem: _spines++; break;
            case FeatureKind.HasNcx:
                if (!FeatureValues.IsTrue(value))
                {
                    _isBackwardCompatible = false;
                }

                break;
            case FeatureKind.RenditionLayout:
                if (resource is null && value is not null)
                {
                    RenditionLayout = value;
                }

                break;
            case FeatureKind.RenditionOrientation:
                if (resource is null && value is not null)
                {
                    RenditionOrientation = value;
                }

                break;
            case FeatureKind.RenditionSpread:
                if (resource is null && value is not null)
                {
                    RenditionSpread = value;
                }

                break;
            case FeatureKind.CharsCount:
                if (FeatureValues.TryLong(value, out long chars))
                {
                    _charsCount += chars;
                }

                break;
            case FeatureKind.DeclaredMimetype:
                if (value is not null && value.StartsWith("audio/", StringComparison.Ordinal))
                {
                    _hasAudio = true;
                }
                else if (value is not null && value.StartsWith("video/", StringComparison.Ordinal))
                {
                    _hasVideo = true;
                }

                break;
            case FeatureKind.FontEmbedded:
                if (value is not null && _embeddedFonts.Add(value))
                {
                    _embeddedFontOrder.Add(value);
                }

                break;
            case FeatureKind.FontReference:
                if (value is not null && _refFonts.Add(value))
                {
                    _refFontOrder.Add(value);
                }

                break;
            case FeatureKind.HasSignatures: _hasSignatures = true; break;
            case FeatureKind.HasEncryption: _hasEncryption = true; break;
            case FeatureKind.DcContributor:
                if (value is not null && _contributors.Add(value))
                {
                    _contributorOrder.Add(value);
                }

                break;
        }
    }

    public void Write(JacksonStyleJsonWriter json)
    {
        json.BeginObject();
        json.Name("publisher");
        json.String(_publisher);
        json.Name("title");
        json.String(_title);
        json.Name("creator");
        json.StringArray(_creator);
        json.Name("date");
        json.String(_date);
        json.Name("subject");
        json.StringArray(_subject);
        json.Name("description");
        json.String(_description);
        json.Name("rights");
        json.String(_rights);
        json.Name("identifier");
        json.String(_identifier);
        json.Name("language");
        json.String(_language);
        json.Name("nSpines");
        json.Number(_spines);
        json.Name("checkSum");
        json.Number(0);
        json.Name("renditionLayout");
        json.String(RenditionLayout);
        json.Name("renditionOrientation");
        json.String(RenditionOrientation);
        json.Name("renditionSpread");
        json.String(RenditionSpread);
        json.Name("ePubVersion");
        json.String(_epubVersion);
        json.Name("isScripted");
        json.Boolean(_isScripted);
        json.Name("hasFixedFormat");
        json.Boolean(_hasFixedFormat);
        json.Name("isBackwardCompatible");
        json.Boolean(_isBackwardCompatible);
        json.Name("hasAudio");
        json.Boolean(_hasAudio);
        json.Name("hasVideo");
        json.Boolean(_hasVideo);
        json.Name("charsCount");
        json.Number(_charsCount);
        json.Name("embeddedFonts");
        json.StringArray(_embeddedFontOrder);
        json.Name("refFonts");
        json.StringArray(_refFontOrder);
        json.Name("hasEncryption");
        json.Boolean(_hasEncryption);
        json.Name("hasSignatures");
        json.Boolean(_hasSignatures);
        json.Name("contributors");
        json.StringArray(_contributorOrder);
        json.EndObject();
    }
}
