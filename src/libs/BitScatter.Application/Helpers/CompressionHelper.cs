using System;
using System.IO;
using System.IO.Compression;

namespace BitScatter.Application.Helpers;

public static class CompressionHelper
{
    public const CompressionLevel DefaultCompressionLevel = CompressionLevel.Fastest;
    public const double DefaultCompressionThreshold = 0.95;

    /// <summary>
    /// Attempts to compress data with Brotli. Returns compressed bytes and whether
    /// compression was kept (adaptive: only if result is smaller than threshold × original).
    /// </summary>
    public static (byte[] Data, bool WasCompressed) TryCompress(
        byte[] plaintext,
        CompressionLevel compressionLevel = DefaultCompressionLevel,
        double threshold = DefaultCompressionThreshold)
    {
        if (plaintext == null || plaintext.Length == 0)
        {
            return (plaintext ?? Array.Empty<byte>(), false);
        }

        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, compressionLevel))
        {
            brotli.Write(plaintext, 0, plaintext.Length);
        }

        var compressed = output.ToArray();

        if (compressed.Length < plaintext.Length * threshold)
            return (compressed, true);

        return (plaintext, false);  // not worth it — return original
    }

    /// <summary>
    /// Decompresses Brotli-compressed data back to original bytes.
    /// </summary>
    public static byte[] Decompress(byte[] compressedData)
    {
        if (compressedData == null || compressedData.Length == 0)
        {
            return compressedData ?? Array.Empty<byte>();
        }

        using var input = new MemoryStream(compressedData);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        brotli.CopyTo(output);
        return output.ToArray();
    }
}
