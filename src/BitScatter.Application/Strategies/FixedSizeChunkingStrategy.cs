using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using BitScatter.Application.DTOs;
using BitScatter.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace BitScatter.Application.Strategies;

public class FixedSizeChunkingStrategy : IChunkingStrategy
{
    private readonly int _chunkSizeBytes;
    private readonly ILogger<FixedSizeChunkingStrategy> _logger;

    public FixedSizeChunkingStrategy(int chunkSizeBytes, ILogger<FixedSizeChunkingStrategy> logger)
    {
        if (chunkSizeBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(chunkSizeBytes), "Chunk size must be positive.");

        _chunkSizeBytes = chunkSizeBytes;
        _logger = logger;
    }

    public async IAsyncEnumerable<ChunkData> ChunkAsync(
        Stream source,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var buffer = new byte[_chunkSizeBytes];
        int chunkIndex = 0;
        int bytesRead;

        _logger.LogDebug("Starting chunking with chunk size {ChunkSize} bytes", _chunkSizeBytes);

        while ((bytesRead = await source.ReadAsync(buffer, 0, _chunkSizeBytes, cancellationToken)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var chunkBytes = buffer[..bytesRead];
            var checksum = ComputeChecksum(chunkBytes);

            _logger.LogDebug("Produced chunk {Index} with {Size} bytes, checksum: {Checksum}",
                chunkIndex, bytesRead, checksum);

            yield return new ChunkData
            {
                Index = chunkIndex++,
                Data = new MemoryStream(chunkBytes),
                Sha256Checksum = checksum,
                Size = bytesRead
            };
        }

        _logger.LogDebug("Chunking complete. Total chunks: {Count}", chunkIndex);
    }

    private static string ComputeChecksum(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
