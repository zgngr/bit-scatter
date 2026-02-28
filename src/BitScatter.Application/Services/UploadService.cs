using System.Collections.Concurrent;
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
    private readonly IChunkingStrategyFactory _chunkingStrategyFactory;
    private readonly ILogger<UploadService> _logger;

    public UploadService(
        IEnumerable<IStorageProvider> storageProviders,
        IFileManifestRepository manifestRepository,
        IChecksumService checksumService,
        IScatteringStrategy scatteringStrategy,
        IChunkingStrategyFactory chunkingStrategyFactory,
        ILogger<UploadService> logger)
    {
        _storageProviders = storageProviders;
        _manifestRepository = manifestRepository;
        _checksumService = checksumService;
        _scatteringStrategy = scatteringStrategy;
        _chunkingStrategyFactory = chunkingStrategyFactory;
        _logger = logger;
    }

    public async Task<UploadResult> UploadAsync(
        string filePath,
        UploadOptions options,
        IProgress<(int completed, int total)>? progress = null,
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

        var selectedProviders = GetSelectedProviders(options.StorageProviders);
        var chunkingStrategy = _chunkingStrategyFactory.Create(options.ChunkSizeBytes);

        var estimatedTotal = (int)Math.Max(1, Math.Ceiling((double)fileInfo.Length / options.ChunkSizeBytes));

        // Phase 1: persist a Pending manifest so it can be tracked even if the upload fails.
        var manifest = new FileManifest
        {
            FileName = fileInfo.Name,
            OriginalSize = fileInfo.Length,
            Sha256Checksum = fileChecksum,
            ChunkSize = options.ChunkSizeBytes,
            Status = ManifestStatus.Pending
        };
        await _manifestRepository.SaveAsync(manifest, cancellationToken);

        var savedChunks = new List<(IStorageProvider Provider, string Key)>();
        var chunkInfos = new List<ChunkInfo>();

        try
        {
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
                    savedChunks.Add((provider, storageKey));

                    chunkInfos.Add(new ChunkInfo
                    {
                        FileManifestId = manifest.Id,
                        ChunkIndex = chunk.Index,
                        Size = chunk.Size,
                        Sha256Checksum = chunk.Sha256Checksum,
                        StorageProviderType = provider.ProviderType,
                        ProviderName = provider.Name,
                        StorageKey = storageKey
                    });

                    progress?.Report((chunkInfos.Count, estimatedTotal));
                }
            }

            // Phase 2: atomically record all chunks and flip status to Complete.
            await _manifestRepository.CompleteAsync(manifest.Id, chunkInfos, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Upload failed for file: {FilePath}. Rolling back {Count} saved chunk(s).",
                filePath, savedChunks.Count);
            await RollbackChunksAsync(savedChunks);
            await TryDeletePendingManifestAsync(manifest.Id);
            throw;
        }

        _logger.LogInformation(
            "Upload complete. File: {FileName}, Id: {ManifestId}, Chunks: {ChunkCount}",
            manifest.FileName, manifest.Id, chunkInfos.Count);

        return new UploadResult
        {
            FileManifestId = manifest.Id,
            FileName = manifest.FileName,
            OriginalSize = manifest.OriginalSize,
            ChunkCount = chunkInfos.Count,
            Success = true
        };
    }

    public async Task<BatchUploadResult> UploadManyAsync(
        IEnumerable<string> filePaths,
        UploadOptions options,
        Func<string, IProgress<(int completed, int total)>?>? progressFactory = null,
        CancellationToken cancellationToken = default)
    {
        var results = new ConcurrentBag<UploadResult>();

        await Parallel.ForEachAsync(filePaths, new ParallelOptions
        {
            MaxDegreeOfParallelism = options.MaxConcurrentUploads,
            CancellationToken = cancellationToken
        }, async (filePath, ct) =>
        {
            try
            {
                results.Add(await UploadAsync(filePath, options, progressFactory?.Invoke(filePath), ct));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Upload failed for file: {FilePath}", filePath);
                results.Add(new UploadResult
                {
                    FileName = Path.GetFileName(filePath),
                    Success = false,
                    ErrorMessage = ex.Message
                });
            }
        });

        var resultList = results.ToList();

        _logger.LogInformation("Batch upload complete. {Success}/{Total} files succeeded.",
            resultList.Count(r => r.Success), resultList.Count);

        return new BatchUploadResult { Results = resultList };
    }

    private async Task RollbackChunksAsync(IReadOnlyList<(IStorageProvider Provider, string Key)> savedChunks)
    {
        foreach (var (provider, key) in savedChunks)
        {
            try
            {
                await provider.DeleteChunkAsync(key);
                _logger.LogDebug("Rolled back chunk {Key} from provider {Name}.", key, provider.Name);
            }
            catch (Exception cleanupEx)
            {
                _logger.LogWarning(cleanupEx,
                    "Failed to roll back chunk {Key} from provider {Name}. The chunk may be orphaned.",
                    key, provider.Name);
            }
        }
    }

    private async Task TryDeletePendingManifestAsync(Guid manifestId)
    {
        try
        {
            await _manifestRepository.DeleteAsync(manifestId);
            _logger.LogDebug("Pending manifest {Id} removed after upload failure.", manifestId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to remove pending manifest {Id}. It will remain as Pending and may be reclaimed by a cleanup process.",
                manifestId);
        }
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
