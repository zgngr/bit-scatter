using System.Security.Cryptography;
using BitScatter.Application.DTOs;
using BitScatter.Application.Interfaces;
using BitScatter.Domain.Enums;
using BitScatter.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace BitScatter.Application.Services;

public class DownloadService : IDownloadService
{
    private readonly IEnumerable<IStorageProvider> _storageProviders;
    private readonly IFileManifestRepository _manifestRepository;
    private readonly ILogger<DownloadService> _logger;

    public DownloadService(
        IEnumerable<IStorageProvider> storageProviders,
        IFileManifestRepository manifestRepository,
        ILogger<DownloadService> logger)
    {
        _storageProviders = storageProviders;
        _manifestRepository = manifestRepository;
        _logger = logger;
    }

    public async Task<DownloadResult> DownloadAsync(
        Guid fileManifestId,
        string outputPath,
        IProgress<(int completed, int total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting download for manifest: {ManifestId}", fileManifestId);

        var manifest = await _manifestRepository.GetByIdAsync(fileManifestId, cancellationToken);
        if (manifest is null)
            throw new KeyNotFoundException($"File manifest with ID '{fileManifestId}' not found.");

        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir))
            Directory.CreateDirectory(outputDir);

        var outputStream = File.Create(outputPath);
        bool downloadSucceeded = false;
        try
        {
            var orderedChunks = manifest.Chunks.OrderBy(c => c.ChunkIndex).ToList();
            int totalChunks = orderedChunks.Count;
            int completedChunks = 0;
            var buf = new byte[81_920];
            using var fileHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            foreach (var chunkInfo in orderedChunks)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var provider = GetProvider(chunkInfo.ProviderName, chunkInfo.StorageProviderType);

                _logger.LogDebug("Reading chunk {Index} from provider {Name} ({ProviderType}) with key {Key}",
                    chunkInfo.ChunkIndex, chunkInfo.ProviderName, chunkInfo.StorageProviderType, chunkInfo.StorageKey);

                await using var chunkStream = await provider.ReadChunkAsync(chunkInfo.StorageKey, cancellationToken);

                using var chunkHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                int read;
                while ((read = await chunkStream.ReadAsync(buf, cancellationToken)) > 0)
                {
                    chunkHash.AppendData(buf, 0, read);
                    fileHash.AppendData(buf, 0, read);
                    await outputStream.WriteAsync(buf.AsMemory(0, read), cancellationToken);
                }

                var actualChecksum = Convert.ToHexString(chunkHash.GetCurrentHash()).ToLowerInvariant();
                if (!string.Equals(actualChecksum, chunkInfo.Sha256Checksum, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ChecksumMismatchException(
                        $"Chunk {chunkInfo.ChunkIndex} integrity check failed.",
                        chunkInfo.Sha256Checksum,
                        actualChecksum);
                }

                progress?.Report((++completedChunks, totalChunks));
            }

            await outputStream.FlushAsync(cancellationToken);

            var finalChecksum = Convert.ToHexString(fileHash.GetCurrentHash()).ToLowerInvariant();
            if (!string.Equals(finalChecksum, manifest.Sha256Checksum, StringComparison.OrdinalIgnoreCase))
            {
                throw new ChecksumMismatchException(
                    "Final file integrity check failed.",
                    manifest.Sha256Checksum,
                    finalChecksum);
            }

            downloadSucceeded = true;
        }
        finally
        {
            await outputStream.DisposeAsync();
            if (!downloadSucceeded && File.Exists(outputPath))
            {
                try { File.Delete(outputPath); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete partial output file: {OutputPath}", outputPath);
                }
            }
        }

        _logger.LogInformation(
            "Download complete. File: {FileName}, saved to: {OutputPath}",
            manifest.FileName, outputPath);

        return new DownloadResult
        {
            FileManifestId = fileManifestId,
            OutputPath = outputPath,
            Success = true
        };
    }

    private IStorageProvider GetProvider(string providerName, StorageProviderType fallbackType)
    {
        // Look up by name first (handles multiple nodes of the same type)
        if (!string.IsNullOrEmpty(providerName))
        {
            var byName = _storageProviders.FirstOrDefault(p => p.Name == providerName);
            if (byName is not null) return byName;
        }

        // Fall back to type-based lookup for backward compatibility
        var byType = _storageProviders.FirstOrDefault(p => p.ProviderType == fallbackType);
        if (byType is null)
            throw new InvalidOperationException($"No storage provider registered for name '{providerName}' or type '{fallbackType}'.");
        return byType;
    }
}
