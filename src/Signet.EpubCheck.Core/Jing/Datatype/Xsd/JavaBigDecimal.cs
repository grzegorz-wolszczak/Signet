using System;
using System.Globalization;
using System.Numerics;

namespace Signet.EpubCheck.Core.Jing.Datatype.Xsd;

/// <summary>
/// The parts of <c>java.math.BigDecimal</c> Jing's datatypes use: an unscaled integer and a scale (the digits after the
/// point, as written: <c>1.10</c> has scale 2). Equality of values is <see cref="CompareTo"/>; <see cref="Equals(JavaBigDecimal)"/>
/// is Java's <c>equals</c>, which also compares the scales.
/// </summary>
internal readonly struct JavaBigDecimal : IComparable<JavaBigDecimal>, IEquatable<JavaBigDecimal>
{
    private const int Radix = 10;

    public JavaBigDecimal(BigInteger unscaled, int scale)
    {
        Unscaled = unscaled;
        Scale = scale;
    }

    public BigInteger Unscaled { get; }

    public int Scale { get; }

    public int Sign => Unscaled.Sign;

    /// <summary>The number of digits of the unscaled value (Jing's <c>movePointRight(scale()).abs().toString().length()</c>).</summary>
    public int Precision => BigInteger.Abs(Unscaled).ToString(CultureInfo.InvariantCulture).Length;

    /// <summary><c>new BigDecimal(s)</c> for a decimal literal without exponent: optional sign, digits, optional point.</summary>
    public static JavaBigDecimal Parse(string s)
    {
        int point = s.IndexOf('.', StringComparison.Ordinal);
        string digits = point < 0 ? s : string.Concat(s.AsSpan(0, point), s.AsSpan(point + 1));
        int scale = point < 0 ? 0 : s.Length - point - 1;
        if (digits is "" or "+" or "-")
        {
            digits += "0";
        }

        return new JavaBigDecimal(BigInteger.Parse(digits, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture), scale);
    }

    public static JavaBigDecimal FromInteger(BigInteger value) => new(value, 0);

    public static JavaBigDecimal operator +(JavaBigDecimal a, JavaBigDecimal b)
    {
        int scale = Math.Max(a.Scale, b.Scale);
        return new JavaBigDecimal(a.Rescale(scale) + b.Rescale(scale), scale);
    }

    public static JavaBigDecimal operator -(JavaBigDecimal a) => new(-a.Unscaled, a.Scale);

    public static bool operator ==(JavaBigDecimal left, JavaBigDecimal right) => left.Equals(right);

    public static bool operator !=(JavaBigDecimal left, JavaBigDecimal right) => !left.Equals(right);

    public static bool operator <(JavaBigDecimal left, JavaBigDecimal right) => left.CompareTo(right) < 0;

    public static bool operator >(JavaBigDecimal left, JavaBigDecimal right) => left.CompareTo(right) > 0;

    public static bool operator <=(JavaBigDecimal left, JavaBigDecimal right) => left.CompareTo(right) <= 0;

    public static bool operator >=(JavaBigDecimal left, JavaBigDecimal right) => left.CompareTo(right) >= 0;

    /// <summary>The integer part (Java's <c>toBigInteger()</c>: towards zero).</summary>
    public BigInteger ToBigInteger() => BigInteger.Divide(Unscaled, BigInteger.Pow(Radix, Scale));

    public int CompareTo(JavaBigDecimal other)
    {
        int scale = Math.Max(Scale, other.Scale);
        return Rescale(scale).CompareTo(other.Rescale(scale));
    }

    public bool Equals(JavaBigDecimal other) => Unscaled == other.Unscaled && Scale == other.Scale;

    public override bool Equals(object? obj) => obj is JavaBigDecimal other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Unscaled, Scale);

    private BigInteger Rescale(int scale) => Unscaled * BigInteger.Pow(Radix, scale - Scale);
}
