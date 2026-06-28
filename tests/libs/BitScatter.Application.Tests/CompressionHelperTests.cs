using System;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using BitScatter.Application.Helpers;
using Xunit;

namespace BitScatter.Application.Tests;

public class CompressionHelperTests
{
    [Fact]
    public void TryCompress_CompressibleData_ReturnsCompressedAndTrue()
    {
        // 10 KB of highly repetitive/compressible data (all 'A')
        var plaintext = new byte[10000];
        Array.Fill(plaintext, (byte)'A');

        var (compressed, wasCompressed) = CompressionHelper.TryCompress(plaintext);

        Assert.True(wasCompressed);
        Assert.True(compressed.Length < plaintext.Length);

        // Verify we can decompress it back to original
        var decompressed = CompressionHelper.Decompress(compressed);
        Assert.Equal(plaintext, decompressed);
    }

    [Fact]
    public void TryCompress_IncompressibleData_ReturnsOriginalAndFalse()
    {
        // 2 KB of random/incompressible data
        var plaintext = new byte[2000];
        RandomNumberGenerator.Fill(plaintext);

        var (compressed, wasCompressed) = CompressionHelper.TryCompress(plaintext);

        Assert.False(wasCompressed);
        Assert.Same(plaintext, compressed); // Should return the exact same array instance
    }

    [Fact]
    public void TryCompress_EmptyData_ReturnsEmptyAndFalse()
    {
        var plaintext = Array.Empty<byte>();

        var (compressed, wasCompressed) = CompressionHelper.TryCompress(plaintext);

        Assert.False(wasCompressed);
        Assert.Equal(plaintext, compressed);
    }

    [Fact]
    public void TryCompress_NullData_ReturnsEmptyAndFalse()
    {
        byte[]? plaintext = null;

        var (compressed, wasCompressed) = CompressionHelper.TryCompress(plaintext!);

        Assert.False(wasCompressed);
        Assert.NotNull(compressed);
        Assert.Empty(compressed);
    }

    [Fact]
    public void Decompress_NullData_ReturnsEmpty()
    {
        byte[]? compressed = null;

        var decompressed = CompressionHelper.Decompress(compressed!);

        Assert.NotNull(decompressed);
        Assert.Empty(decompressed);
    }

    [Fact]
    public void TryCompress_CustomThreshold_RespectsThreshold()
    {
        // Repetitive data but we set threshold to a very low value (e.g. 0.1) so compression is rejected
        var plaintext = new byte[1000];
        Array.Fill(plaintext, (byte)'A');

        var (compressed, wasCompressed) = CompressionHelper.TryCompress(plaintext, CompressionLevel.Optimal, 0.01);

        // It shouldn't be compressed because compressed size won't be less than 1% of original
        Assert.False(wasCompressed);
        Assert.Same(plaintext, compressed);
    }
}
