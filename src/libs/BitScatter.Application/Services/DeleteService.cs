using BitScatter.Application.DTOs;
using BitScatter.Application.Interfaces;
using BitScatter.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace BitScatter.Application.Services;

public class DeleteService : IDeleteService
{
    private readonly IEnumerable<IStorageProvider> _storageProviders;
    private readonly IFileManifestRepository _manifestRepository;
    private readonly ILogger<DeleteService> _logger;

    public DeleteService(
        IEnumerable<IStorageProvider> storageProviders,
        IFileManifestRepository manifestRepository,
        ILogger<DeleteService> logger)
    {
        _storageProviders = storageProviders;
        _manifestRepository = manifestRepository;
        _logger = logger;
    }

    public async Task<DeleteResult> DeleteAsync(Guid fileManifestId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting deletion for manifest: {ManifestId}", fileManifestId);

        try
        {
            var manifest = await _manifestRepository.GetByIdAsync(fileManifestId, cancellationToken);
            if (manifest is null)
                throw new KeyNotFoundException($"File manifest with ID '{fileManifestId}' not found.");

            var orderedChunks = manifest.Chunks.OrderBy(c => c.ChunkIndex).ToList();
            foreach (var chunk in orderedChunks)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var provider = GetProvider(chunk.ProviderName, chunk.StorageProviderType);
                _logger.LogDebug(
                    "Deleting chunk {Index} from provider {Name} ({ProviderType}) with key {Key}",
                    chunk.ChunkIndex, chunk.ProviderName, chunk.StorageProviderType, chunk.StorageKey);

                await provider.DeleteChunkAsync(chunk.StorageKey, cancellationToken);
            }

            await _manifestRepository.DeleteAsync(fileManifestId, cancellationToken);

            _logger.LogInformation(
                "Deletion complete. Manifest: {ManifestId}, File: {FileName}, Chunks: {ChunkCount}",
                fileManifestId, manifest.FileName, orderedChunks.Count);

            return new DeleteResult { FileManifestId = fileManifestId, Success = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete file manifest: {ManifestId}", fileManifestId);
            return new DeleteResult
            {
                FileManifestId = fileManifestId,
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private IStorageProvider GetProvider(string providerName, StorageProviderType fallbackType)
    {
        if (!string.IsNullOrEmpty(providerName))
        {
            var byName = _storageProviders.FirstOrDefault(p => p.Name == providerName);
            if (byName is not null) return byName;
        }

        var byType = _storageProviders.FirstOrDefault(p => p.ProviderType == fallbackType);
        if (byType is null)
            throw new InvalidOperationException($"No storage provider registered for name '{providerName}' or type '{fallbackType}'.");
        return byType;
    }
}
