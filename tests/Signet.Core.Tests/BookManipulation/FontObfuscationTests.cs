using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using AwesomeAssertions;
using Signet.Core.Misc;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>Tests of <see cref="FontObfuscation"/> — font (de)obfuscation with the IDPF and Adobe methods.</summary>
public sealed class FontObfuscationTests
{
    private const string Idpf = OcfReader.IdpfFontAlgorithmId;
    private const string Adobe = OcfReader.AdobeFontAlgorithmId;
    private const string Identifier = "urn:uuid:0f0e0d0c-0b0a-0908-0706-050403020100";

    [Theory]
    [InlineData(Idpf, FontObfuscation.IdpfMethodNumBytes)]
    [InlineData(Adobe, FontObfuscation.AdobeMethodNumBytes)]
    public void ObfuscateFile_IsInvolution_AndTouchesOnlyLeadingBytes(string algorithm, int span)
    {
        using TempDir temp = new();
        byte[] original = DeterministicBytes(4096, seed: 7);
        string path = temp.Combine("font.bin");
        File.WriteAllBytes(path, original);

        FontObfuscation.ObfuscateFile(path, algorithm, Identifier);
        byte[] obfuscated = File.ReadAllBytes(path);

        obfuscated.Should().NotEqual(original, "the first bytes should be scrambled");
        obfuscated[span..].Should().Equal(original[span..], "bytes outside the method range must stay untouched");
        obfuscated[..span].Should().NotEqual(original[..span]);

        FontObfuscation.ObfuscateFile(path, algorithm, Identifier);
        File.ReadAllBytes(path).Should().Equal(original, "the operation is an involution: f(f(x)) = x");
    }

    [Fact]
    public void ObfuscateFile_ShorterThanMethodSpan_StillRoundTrips()
    {
        using TempDir temp = new();
        byte[] original = DeterministicBytes(64, seed: 3);
        string path = temp.Combine("tiny.bin");
        File.WriteAllBytes(path, original);

        FontObfuscation.ObfuscateFile(path, Idpf, Identifier);
        File.ReadAllBytes(path).Should().NotEqual(original);

        FontObfuscation.ObfuscateFile(path, Idpf, Identifier);
        File.ReadAllBytes(path).Should().Equal(original);
    }

    [Fact]
    public void IdpfKeyFromIdentifier_StripsWhitespace_AndMatchesSha1()
    {
        byte[] spaced = FontObfuscation.IdpfKeyFromIdentifier(" abc\t123\r\n");
        byte[] tight = FontObfuscation.IdpfKeyFromIdentifier("abc123");

        spaced.Should().Equal(tight);
#pragma warning disable CA5350
        spaced.Should().Equal(SHA1.HashData(Encoding.Latin1.GetBytes("abc123")));
#pragma warning restore CA5350
        spaced.Should().HaveCount(20);
    }

    [Fact]
    public void AdobeKeyFromIdentifier_StripsUrnPrefixDashesAndColons()
    {
        byte[] key = FontObfuscation.AdobeKeyFromIdentifier("urn:uuid:0f0e0d0c-0b0a-0908-0706-050403020100");

        key.Should().HaveCount(16);
        key.Should().Equal(Convert.FromHexString("0f0e0d0c0b0a09080706050403020100"));
    }

    [Fact]
    public void AdobeKeyFromIdentifier_NonHexIdentifier_ReturnsEmptyKey() =>
        FontObfuscation.AdobeKeyFromIdentifier("not-a-uuid-value").Should().BeEmpty();

    [Fact]
    public void ObfuscateFile_MissingFile_Throws()
    {
        Action act = () => FontObfuscation.ObfuscateFile("Z:/nope/missing.ttf", Idpf, Identifier);
        act.Should().Throw<FontObfuscationException>();
    }

    [Fact]
    public void ObfuscateFile_EmptyIdentifier_Throws()
    {
        using TempDir temp = new();
        string path = temp.Combine("f.ttf");
        File.WriteAllBytes(path, DeterministicBytes(32, seed: 1));

        Action act = () => FontObfuscation.ObfuscateFile(path, Idpf, string.Empty);
        act.Should().Throw<FontObfuscationException>();
    }

    [Fact]
    public void ObfuscateFile_EmptyAlgorithm_Throws()
    {
        using TempDir temp = new();
        string path = temp.Combine("f.ttf");
        File.WriteAllBytes(path, DeterministicBytes(32, seed: 1));

        Action act = () => FontObfuscation.ObfuscateFile(path, string.Empty, Identifier);
        act.Should().Throw<FontObfuscationException>();
    }

    [Fact]
    public void ObfuscateFile_UnknownAlgorithm_Throws()
    {
        using TempDir temp = new();
        string path = temp.Combine("f.ttf");
        File.WriteAllBytes(path, DeterministicBytes(32, seed: 1));

        Action act = () => FontObfuscation.ObfuscateFile(path, "http://example.com/enc", Identifier);
        act.Should().Throw<FontObfuscationException>();
    }

    private static byte[] DeterministicBytes(int count, int seed)
    {
        byte[] data = new byte[count];
        for (int i = 0; i < count; i++)
        {
            data[i] = (byte)((i * 31 + seed * 17 + 3) % 256);
        }

        return data;
    }
}
