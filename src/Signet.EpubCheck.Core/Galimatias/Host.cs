using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Signet.EpubCheck.Core.Galimatias;

/// <summary>The host of a URL: a <see cref="Domain"/>, an <see cref="IPv4Address"/> or an <see cref="IPv6Address"/> (galimatias' <c>Host</c>).</summary>
public abstract class Host
{
    /// <summary>The serialization of the host (ASCII; an IPv6 address without brackets).</summary>
    public abstract override string ToString();

    /// <summary>The host as a person reads it (a domain in Unicode).</summary>
    public abstract string ToHumanString();

    /// <summary>Parses a host: an IPv6 address in brackets, an IPv4 address or a domain.</summary>
    /// <exception cref="GalimatiasParseException">The host is not valid.</exception>
    public static Host ParseHost(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Length == 0)
        {
            throw new GalimatiasParseException("empty host", -1);
        }

        if (input[0] == '[')
        {
            if (input[^1] != ']')
            {
                throw new GalimatiasParseException("Unmatched '['", -1);
            }

            return IPv6Address.ParseIPv6Address(input[1..^1]);
        }

        Domain domain = Domain.ParseDomain(input);
        return IPv4Address.TryParseIPv4Address(domain.ToString(), out IPv4Address? address) ? address : domain;
    }
}

/// <summary>A domain name, in its ASCII form (galimatias' <c>Domain</c>).</summary>
public sealed class Domain : Host, IEquatable<Domain>
{
    // The characters that are not allowed in a domain once it is in ASCII.
    private const string ForbiddenCharacters = "\0\t\n\r #%/:?@[\\]";

    private readonly string _domain;
    private readonly bool _unicode;

    private Domain(string domain, bool unicode)
    {
        _domain = domain;
        _unicode = unicode;
    }

    /// <summary>Parses a domain: percent-decoded, then UTS #46 ToASCII (ToUnicode as well when <paramref name="unicode"/> is set).</summary>
    /// <exception cref="GalimatiasParseException">The domain is empty or not valid.</exception>
    public static Domain ParseDomain(string input, bool unicode = false)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Length == 0)
        {
            throw new GalimatiasParseException("input is empty");
        }

        string asciiDomain = UrlUtils.DomainToAscii(UrlUtils.PercentDecode(input));
        foreach (char c in asciiDomain)
        {
            if (ForbiddenCharacters.Contains(c, StringComparison.Ordinal))
            {
                throw new GalimatiasParseException("Illegal character in domain" + UrlParser.DescribeIllegal(c));
            }
        }

        return unicode ? new Domain(UrlUtils.DomainToUnicode(asciiDomain), unicode) : new Domain(asciiDomain, unicode);
    }

    /// <inheritdoc />
    public override string ToString() => _domain;

    /// <inheritdoc />
    public override string ToHumanString()
    {
        if (_unicode)
        {
            return _domain;
        }

        try
        {
            return UrlUtils.DomainToUnicode(_domain);
        }
        catch (GalimatiasParseException)
        {
            return _domain;
        }
    }

    /// <inheritdoc />
    public bool Equals(Domain? other) => other is not null && _domain == other._domain;

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as Domain);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(_domain);
}

/// <summary>An IPv4 address in its dotted-decimal form (galimatias' <c>IPv4Address</c>).</summary>
public sealed class IPv4Address : Host, IEquatable<IPv4Address>
{
    private const int Octets = 4;
    private const int MaxOctet = byte.MaxValue;
    private const int DecimalBase = 10;

    private readonly byte[] _octets;

    private IPv4Address(byte[] octets)
    {
        _octets = octets;
    }

    /// <summary>Parses four decimal numbers up to 255 separated by dots.</summary>
    /// <exception cref="GalimatiasParseException">The input is not such an address.</exception>
    public static IPv4Address ParseIPv4Address(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Length == 0)
        {
            throw new GalimatiasParseException("empty input");
        }

        byte[] octets = new byte[Octets];
        int dotsSeen = 0;
        int i = 0;
        while (i < input.Length)
        {
            char c = input[i];
            int value = 0;
            while (UrlUtils.IsAsciiDigit(c))
            {
                value = (value * DecimalBase) + (c - '0');
                i++;
                c = i >= input.Length ? '\0' : input[i];
                if (value > MaxOctet)
                {
                    break;
                }
            }

            if (value > MaxOctet)
            {
                throw new GalimatiasParseException("Malformed IPv4 address, bad value: " + value.ToString(CultureInfo.InvariantCulture));
            }

            if (dotsSeen < Octets - 1 && c != '.')
            {
                throw new GalimatiasParseException("Malformed IPv4 address", i);
            }

            if (dotsSeen == Octets - 1 && i < input.Length)
            {
                throw new GalimatiasParseException("IPv4 address is too long", i);
            }

            if (dotsSeen >= Octets)
            {
                throw new GalimatiasParseException("Malformed IPv4 address", i);
            }

            octets[dotsSeen] = (byte)value;
            dotsSeen++;
            i++;
        }

        if (dotsSeen != Octets)
        {
            throw new GalimatiasParseException("Malformed IPv4 address");
        }

        return new IPv4Address(octets);
    }

    /// <summary>Like <see cref="ParseIPv4Address"/>, without the exception.</summary>
    public static bool TryParseIPv4Address(string input, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IPv4Address? address)
    {
        try
        {
            address = ParseIPv4Address(input);
            return true;
        }
        catch (GalimatiasParseException)
        {
            address = null;
            return false;
        }
    }

    /// <inheritdoc />
    public override string ToString() => string.Join('.', _octets.Select(octet => octet.ToString(CultureInfo.InvariantCulture)));

    /// <inheritdoc />
    public override string ToHumanString() => ToString();

    /// <inheritdoc />
    public bool Equals(IPv4Address? other) => other is not null && _octets.AsSpan().SequenceEqual(other._octets);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as IPv4Address);

    /// <inheritdoc />
    public override int GetHashCode() => BitConverter.ToInt32(_octets);
}

/// <summary>An IPv6 address (galimatias' <c>IPv6Address</c>, after the URL standard).</summary>
public sealed class IPv6Address : Host, IEquatable<IPv6Address>
{
    private const int Pieces = 8;
    private const int IPv4PiecesStart = Pieces - 2; // an embedded IPv4 address takes the last two pieces
    private const int HexDigitsPerPiece = 4;
    private const int HexBase = 16;
    private const int DecimalBase = 10;
    private const int MaxOctet = byte.MaxValue;
    private const int BitsPerByte = 8;
    private const int IPv4Dots = 3;

    private readonly ushort[] _pieces;

    private IPv6Address(ushort[] pieces)
    {
        _pieces = pieces;
    }

    /// <summary>Parses an IPv6 address (without its brackets), also with an embedded IPv4 address.</summary>
    /// <exception cref="GalimatiasParseException">The input is not an IPv6 address.</exception>
    public static IPv6Address ParseIPv6Address(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Length == 0)
        {
            throw new GalimatiasParseException("empty string");
        }

        ushort[] address = new ushort[Pieces];
        int piecePointer = 0;
        int? compressPointer = null;
        int idx = 0;
        bool isEof = idx >= input.Length;
        char c = isEof ? '\0' : input[idx];
        if (c == ':')
        {
            if (idx + 1 >= input.Length || input[idx + 1] != ':')
            {
                throw new GalimatiasParseException("IPv6 address starting with ':' is not followed by a second ':'.");
            }

            idx += 2;
            piecePointer = 1;
            compressPointer = piecePointer;
        }

        bool jumpToIPv4 = false;
        while (!isEof)
        {
            isEof = idx >= input.Length;
            c = isEof ? '\0' : input[idx];
            if (piecePointer == Pieces)
            {
                throw new GalimatiasParseException("Address too long");
            }

            if (c == ':')
            {
                if (compressPointer is not null)
                {
                    throw new GalimatiasParseException("Zero-compression can be used only once.");
                }

                idx++;
                isEof = idx >= input.Length;
                piecePointer++;
                compressPointer = piecePointer;
                continue;
            }

            int value = 0;
            int length = 0;
            while (length < HexDigitsPerPiece && UrlUtils.IsAsciiHexDigit(c))
            {
                value = (value * HexBase) + Convert.ToInt32(c.ToString(), HexBase);
                idx++;
                isEof = idx >= input.Length;
                c = isEof ? '\0' : input[idx];
                length++;
            }

            if (c == '.')
            {
                if (length == 0)
                {
                    throw new GalimatiasParseException("':' cannot be followed by '.'");
                }

                idx -= length;
                isEof = idx >= input.Length;
                c = isEof ? '\0' : input[idx];
                jumpToIPv4 = true;
                break;
            }
            else if (c == ':')
            {
                idx++;
                isEof = idx >= input.Length;
                if (isEof)
                {
                    throw new GalimatiasParseException("Cannot end with ':'");
                }
            }
            else if (!isEof)
            {
                throw new GalimatiasParseException("Illegal character");
            }

            address[piecePointer] = (ushort)value;
            piecePointer++;
        }

        bool jumpToFinale = !jumpToIPv4 && isEof;
        if (!jumpToFinale && piecePointer > IPv4PiecesStart)
        {
            throw new GalimatiasParseException("Not enough room for a IPv4-mapped address");
        }

        int dotsSeen = 0;
        if (!jumpToFinale)
        {
            while (!isEof)
            {
                int value = 0;
                if (!UrlUtils.IsAsciiDigit(c))
                {
                    throw new GalimatiasParseException("Non-digit character in IPv4-mapped address");
                }

                while (UrlUtils.IsAsciiDigit(c))
                {
                    value = (value * DecimalBase) + (c - '0');
                    idx++;
                    isEof = idx >= input.Length;
                    c = isEof ? '\0' : input[idx];
                    if (value > MaxOctet)
                    {
                        break;
                    }
                }

                if (value > MaxOctet)
                {
                    throw new GalimatiasParseException("Invalid value for IPv4-mapped address");
                }

                if (dotsSeen < IPv4Dots && c != '.')
                {
                    throw new GalimatiasParseException("Illegal character in IPv4-mapped address");
                }

                address[piecePointer] = (ushort)((address[piecePointer] << BitsPerByte) + value);
                if (dotsSeen is 1 or IPv4Dots)
                {
                    piecePointer++;
                }

                idx++;
                isEof = idx >= input.Length;
                c = isEof ? '\0' : input[idx];
                if (dotsSeen == IPv4Dots && !isEof)
                {
                    throw new GalimatiasParseException("Too long IPv4-mapped address");
                }

                dotsSeen++;
            }
        }

        if (compressPointer is int compress)
        {
            int swaps = piecePointer - compress;
            piecePointer = Pieces - 1;
            while (swaps != 0)
            {
                (address[piecePointer], address[compress + swaps - 1]) = (address[compress + swaps - 1], address[piecePointer]);
                piecePointer--;
                swaps--;
            }
        }
        else if (piecePointer != Pieces)
        {
            throw new GalimatiasParseException("Address too short");
        }

        return new IPv6Address(address);
    }

    /// <summary>The serialization of the URL standard: lower-case hexadecimal, the first longest run of zero pieces as <c>::</c>.</summary>
    public override string ToString()
    {
        // The first longest run of zero pieces is compressed. The URL standard compresses only runs longer than one
        // piece; galimatias also compresses a single zero piece (upstream behaviour, kept).
        int compressPointer = -1;
        int maxConsecutiveZeroes = 0;
        for (int i = 0; i < _pieces.Length; i++)
        {
            if (_pieces[i] != 0)
            {
                continue;
            }

            int consecutiveZeroes = 0;
            for (int j = i; j < _pieces.Length && _pieces[j] == 0; j++)
            {
                consecutiveZeroes++;
            }

            if (consecutiveZeroes > maxConsecutiveZeroes)
            {
                compressPointer = i;
                maxConsecutiveZeroes = consecutiveZeroes;
            }
        }

        StringBuilder output = new();
        for (int i = 0; i < _pieces.Length; i++)
        {
            if (compressPointer == i)
            {
                output.Append(i == 0 ? "::" : ":");
                while (i < _pieces.Length && _pieces[i] == 0)
                {
                    i++;
                }
            }

            if (i >= _pieces.Length)
            {
                break;
            }

            output.Append(_pieces[i].ToString("x", CultureInfo.InvariantCulture));
            if (i < _pieces.Length - 1)
            {
                output.Append(':');
            }
        }

        return output.ToString();
    }

    /// <inheritdoc />
    public override string ToHumanString() => ToString();

    /// <inheritdoc />
    public bool Equals(IPv6Address? other) => other is not null && _pieces.AsSpan().SequenceEqual(other._pieces);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as IPv6Address);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        HashCode hash = new();
        foreach (ushort piece in _pieces)
        {
            hash.Add(piece);
        }

        return hash.ToHashCode();
    }
}
