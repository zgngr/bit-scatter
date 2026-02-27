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
    private readonly IScatteringStrategy _scatteringStrategy;
    private readonly ILogger<UploadService> _logger;

    public UploadService(
        IEnumerable<IStorageProvider> storageProviders,
        IFileManifestRepository manifestRepository,
        IChecksumService checksumService,
        IScatteringStrategy scatteringStrategy,
        ILogger<UploadService> logger)
    {
        _storageProviders = storageProviders;
        _manifestRepository = manifestRepository;
        _checksumService = checksumService;
        _scatteringStrategy = scatteringStrategy;
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

        await foreach (var chunk in chunkingStrategy.ChunkAsync(fileStream, cancellationToken))
        {
            using (chunk)
            {
                var provider = _scatteringStrategy.SelectProvider(chunk.Index, selectedProviders);
                var storageKey = $"{manifest.Id}/{chunk.Index}";

                _logger.LogDebug("Saving chunk {Index} to provider {Name} ({ProviderType}) with key {Key}",
                    chunk.Index, provider.Name, provider.ProviderType, storageKey);

                await provider.SaveChunkAsync(chunk.Data, storageKey, cancellationToken);

                manifest.Chunks.Add(new ChunkInfo
                {
                    FileManifestId = manifest.Id,
                    ChunkIndex = chunk.Index,
                    Size = chunk.Size,
                    Sha256Checksum = chunk.Sha256Checksum,
                    StorageProviderType = provider.ProviderType,
                    ProviderName = provider.Name,
                    StorageKey = storageKey
                });
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

    private IReadOnlyList<IStorageProvider> GetSelectedProviders(StorageProviderType[]? types)
    {
        if (types is null || types.Length == 0)
        {
            var all = _storageProviders.ToList();
            if (all.Count == 0)
                throw new InvalidOperationException("No storage providers are registered.");
            _logger.LogDebug("Scattering across all {Count} available providers.", all.Count);
            return all;
        }

        var providers = _storageProviders
            .Where(p => types.Contains(p.ProviderType))
            .ToList();

        if (providers.Count == 0)
            throw new InvalidOperationException($"No storage providers found for types: {string.Join(", ", types)}");

        return providers;
    }
}
