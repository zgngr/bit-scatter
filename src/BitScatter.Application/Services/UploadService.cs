using BitScatter.Application.DTOs;
using BitScatter.Application.Interfaces;
using BitScatter.Domain.Entities;
using BitScatter.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace BitScatter.Application.Services;

public class UploadService : IUploadService
{
    private readonly IEnumerable<IStorageProvider> _storageProviders;
    private readonly IFileManifestRepository _manifestRepository;
    private readonly IChecksumService _checksumService;
    private readonly ILogger<UploadService> _logger;

    public UploadService(
        IEnumerable<IStorageProvider> storageProviders,
        IFileManifestRepository manifestRepository,
        IChecksumService checksumService,
        ILogger<UploadService> logger)
    {
        _storageProviders = storageProviders;
        _manifestRepository = manifestRepository;
        _checksumService = checksumService;
        _logger = logger;
    }

    public async Task<UploadResult> UploadAsync(
        string filePath,
        UploadOptions options,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}", filePath);

        _logger.LogInformation("Starting upload for file: {FilePath}", filePath);

        var fileInfo = new FileInfo(filePath);
        string fileChecksum;

        await using (var checksumStream = File.OpenRead(filePath))
        {
            fileChecksum = await _checksumService.ComputeSha256Async(checksumStream, cancellationToken);
        }

        _logger.LogDebug("File checksum computed: {Checksum}", fileChecksum);

        var manifest = new FileManifest
        {
            FileName = fileInfo.Name,
            OriginalSize = fileInfo.Length,
            Sha256Checksum = fileChecksum,
            ChunkSize = options.ChunkSizeBytes
        };

        var selectedProviders = GetSelectedProviders(options.StorageProviders);
        var chunkingStrategy = new Strategies.FixedSizeChunkingStrategy(
            options.ChunkSizeBytes,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Strategies.FixedSizeChunkingStrategy>.Instance);

        await using var fileStream = File.OpenRead(filePath);
        int providerIndex = 0;

        await foreach (var chunk in chunkingStrategy.ChunkAsync(fileStream, cancellationToken))
        {
            using (chunk)
            {
                var provider = selectedProviders[providerIndex % selectedProviders.Count];
                var storageKey = $"{manifest.Id}/{chunk.Index}";

                _logger.LogDebug("Saving chunk {Index} to provider {ProviderType} with key {Key}",
                    chunk.Index, provider.ProviderType, storageKey);

                await provider.SaveChunkAsync(chunk.Data, storageKey, cancellationToken);

                manifest.Chunks.Add(new ChunkInfo
                {
                    FileManifestId = manifest.Id,
                    ChunkIndex = chunk.Index,
                    Size = chunk.Size,
                    Sha256Checksum = chunk.Sha256Checksum,
                    StorageProviderType = provider.ProviderType,
                    StorageKey = storageKey
                });

                providerIndex++;
            }
        }

        await _manifestRepository.SaveAsync(manifest, cancellationToken);

        _logger.LogInformation(
            "Upload complete. File: {FileName}, Id: {ManifestId}, Chunks: {ChunkCount}",
            manifest.FileName, manifest.Id, manifest.Chunks.Count);

        return new UploadResult
        {
            FileManifestId = manifest.Id,
            FileName = manifest.FileName,
            OriginalSize = manifest.OriginalSize,
            ChunkCount = manifest.Chunks.Count,
            Success = true
        };
    }

    private List<IStorageProvider> GetSelectedProviders(StorageProviderType[] types)
    {
        var providers = _storageProviders
            .Where(p => types.Contains(p.ProviderType))
            .ToList();

        if (providers.Count == 0)
            throw new InvalidOperationException($"No storage providers found for types: {string.Join(", ", types)}");

        return providers;
    }
}
