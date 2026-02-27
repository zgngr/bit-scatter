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
    private readonly IChecksumService _checksumService;
    private readonly ILogger<DownloadService> _logger;

    public DownloadService(
        IEnumerable<IStorageProvider> storageProviders,
        IFileManifestRepository manifestRepository,
        IChecksumService checksumService,
        ILogger<DownloadService> logger)
    {
        _storageProviders = storageProviders;
        _manifestRepository = manifestRepository;
        _checksumService = checksumService;
        _logger = logger;
    }

    public async Task<DownloadResult> DownloadAsync(
        Guid fileManifestId,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting download for manifest: {ManifestId}", fileManifestId);

        var manifest = await _manifestRepository.GetByIdAsync(fileManifestId, cancellationToken);
        if (manifest is null)
            throw new KeyNotFoundException($"File manifest with ID '{fileManifestId}' not found.");

        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir))
            Directory.CreateDirectory(outputDir);

        await using var outputStream = File.Create(outputPath);

        var orderedChunks = manifest.Chunks.OrderBy(c => c.ChunkIndex).ToList();

        foreach (var chunkInfo in orderedChunks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var provider = GetProvider(chunkInfo.ProviderName, chunkInfo.StorageProviderType);

            _logger.LogDebug("Reading chunk {Index} from provider {Name} ({ProviderType}) with key {Key}",
                chunkInfo.ChunkIndex, chunkInfo.ProviderName, chunkInfo.StorageProviderType, chunkInfo.StorageKey);

            await using var chunkStream = await provider.ReadChunkAsync(chunkInfo.StorageKey, cancellationToken);

            var chunkBytes = new byte[chunkStream.Length];
            await chunkStream.ReadExactlyAsync(chunkBytes, cancellationToken);

            var actualChecksum = _checksumService.ComputeSha256(chunkBytes);
            if (!string.Equals(actualChecksum, chunkInfo.Sha256Checksum, StringComparison.OrdinalIgnoreCase))
            {
                throw new ChecksumMismatchException(
                    $"Chunk {chunkInfo.ChunkIndex} integrity check failed.",
                    chunkInfo.Sha256Checksum,
                    actualChecksum);
            }

            await outputStream.WriteAsync(chunkBytes, cancellationToken);
        }

        await outputStream.FlushAsync(cancellationToken);
        outputStream.Seek(0, SeekOrigin.Begin);

        var finalChecksum = await _checksumService.ComputeSha256Async(outputStream, cancellationToken);
        if (!string.Equals(finalChecksum, manifest.Sha256Checksum, StringComparison.OrdinalIgnoreCase))
        {
            throw new ChecksumMismatchException(
                "Final file integrity check failed.",
                manifest.Sha256Checksum,
                finalChecksum);
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
