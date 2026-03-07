using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Threading.Channels;
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
    private readonly IScatteringStrategy _scatteringStrategy;
    private readonly IChunkingStrategyFactory _chunkingStrategyFactory;
    private readonly ILogger<UploadService> _logger;

    public UploadService(
        IEnumerable<IStorageProvider> storageProviders,
        IFileManifestRepository manifestRepository,
        IScatteringStrategy scatteringStrategy,
        IChunkingStrategyFactory chunkingStrategyFactory,
        ILogger<UploadService> logger)
    {
        _storageProviders = storageProviders;
        _manifestRepository = manifestRepository;
        _scatteringStrategy = scatteringStrategy;
        _chunkingStrategyFactory = chunkingStrategyFactory;
        _logger = logger;
    }

    private async Task<UploadResult> UploadSingleCoreAsync(
        string filePath,
        UploadOptions options,
        IProgress<(int completed, int total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}", filePath);

        _logger.LogInformation("Starting upload for file: {FilePath}", filePath);

        var fileInfo = new FileInfo(filePath);

        var selectedProviders = GetSelectedProviders(options.StorageProviders);
        var chunkingStrategy = _chunkingStrategyFactory.Create(options.ChunkSizeBytes);

        var estimatedTotal = (int)Math.Max(1, Math.Ceiling((double)fileInfo.Length / options.ChunkSizeBytes));

        // Phase 1: persist a Pending manifest so it can be tracked even if the upload fails.
        var manifest = new FileManifest
        {
            FileName = fileInfo.Name,
            OriginalSize = fileInfo.Length,
            Sha256Checksum = string.Empty,
            ChunkSize = options.ChunkSizeBytes,
            Status = ManifestStatus.Pending
        };
        await _manifestRepository.SaveAsync(manifest, cancellationToken);

        var savedChunks = new ConcurrentBag<(IStorageProvider Provider, string Key)>();
        var chunkInfos = new ConcurrentBag<ChunkInfo>();
        string fileChecksum = string.Empty;

        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var pipelineToken = linkedCts.Token;
            var channel = Channel.CreateBounded<PendingChunk>(new BoundedChannelOptions(options.MaxInFlightChunks)
            {
                SingleWriter = true,
                SingleReader = false,
                FullMode = BoundedChannelFullMode.Wait
            });

            ExceptionDispatchInfo? firstFailure = null;

            void SignalFailure(Exception ex)
            {
                if (Interlocked.CompareExchange(ref firstFailure, ExceptionDispatchInfo.Capture(ex), null) is null)
                {
                    channel.Writer.TryComplete(ex);
                    linkedCts.Cancel();
                }
            }

            var producerTask = Task.Run(async () =>
            {
                try
                {
                    await using var fileStream = File.OpenRead(filePath);
                    using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    var completed = 0;

                    await foreach (var chunk in chunkingStrategy.ChunkAsync(fileStream, pipelineToken))
                    {
                        hasher.AppendData(ReadChunkBytes(chunk));

                        var provider = _scatteringStrategy.SelectProvider(chunk.Index, selectedProviders);
                        var storageKey = $"{manifest.Id}/{chunk.Index}";

                        _logger.LogDebug(
                            "Enqueuing chunk {Index} for provider {Name} ({ProviderType}) with key {Key}",
                            chunk.Index, provider.Name, provider.ProviderType, storageKey);

                        await channel.Writer.WriteAsync(
                            new PendingChunk(chunk, provider, storageKey),
                            pipelineToken);

                        completed++;
                        progress?.Report((completed, estimatedTotal));
                    }

                    fileChecksum = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
                    channel.Writer.TryComplete();
                }
                catch (OperationCanceledException) when (pipelineToken.IsCancellationRequested)
                {
                    if (cancellationToken.IsCancellationRequested)
                        throw;
                    channel.Writer.TryComplete();
                }
                catch (Exception ex)
                {
                    SignalFailure(ex);
                }
            }, CancellationToken.None);

            var consumerTasks = Enumerable.Range(0, options.MaxInFlightChunks)
                .Select(_ => Task.Run(async () =>
                {
                    try
                    {
                        await foreach (var pending in channel.Reader.ReadAllAsync(pipelineToken))
                        {
                            using (pending.Chunk)
                            {
                                _logger.LogDebug(
                                    "Saving chunk {Index} to provider {Name} ({ProviderType}) with key {Key}",
                                    pending.Chunk.Index,
                                    pending.Provider.Name,
                                    pending.Provider.ProviderType,
                                    pending.StorageKey);

                                await pending.Provider.SaveChunkAsync(pending.Chunk.Data, pending.StorageKey, pipelineToken);
                                savedChunks.Add((pending.Provider, pending.StorageKey));

                                chunkInfos.Add(new ChunkInfo
                                {
                                    FileManifestId = manifest.Id,
                                    ChunkIndex = pending.Chunk.Index,
                                    Size = pending.Chunk.Size,
                                    Sha256Checksum = pending.Chunk.Sha256Checksum,
                                    StorageProviderType = pending.Provider.ProviderType,
                                    ProviderName = pending.Provider.Name,
                                    StorageKey = pending.StorageKey
                                });
                            }
                        }
                    }
                    catch (OperationCanceledException) when (pipelineToken.IsCancellationRequested)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            throw;
                        // Pipeline was canceled due to first failure.
                    }
                    catch (Exception ex)
                    {
                        SignalFailure(ex);
                    }
                }, CancellationToken.None))
                .ToArray();

            await producerTask;
            await Task.WhenAll(consumerTasks);

            if (firstFailure is not null)
                firstFailure.Throw();

            var orderedChunkInfos = chunkInfos
                .OrderBy(c => c.ChunkIndex)
                .ToList();

            // Phase 2: atomically record all chunks and flip status to Complete.
            await _manifestRepository.CompleteAsync(manifest.Id, fileChecksum, orderedChunkInfos, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Upload failed for file: {FilePath}. Rolling back {Count} saved chunk(s).",
                filePath, savedChunks.Count);
            await RollbackChunksAsync(savedChunks.ToList());
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

    private static byte[] ReadChunkBytes(ChunkData chunk)
    {
        if (chunk.Data is MemoryStream ms)
            return ms.ToArray();

        if (!chunk.Data.CanSeek)
            throw new InvalidOperationException("Chunk stream must be seekable to compute streaming file checksum.");

        var originalPosition = chunk.Data.Position;
        try
        {
            using var copy = new MemoryStream();
            chunk.Data.CopyTo(copy);
            return copy.ToArray();
        }
        finally
        {
            chunk.Data.Position = originalPosition;
        }
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
                results.Add(await UploadSingleCoreAsync(filePath, options, progressFactory?.Invoke(filePath), ct));
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

    private sealed record PendingChunk(ChunkData Chunk, IStorageProvider Provider, string StorageKey);
}
