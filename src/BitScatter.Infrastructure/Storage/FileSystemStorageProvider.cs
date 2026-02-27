using BitScatter.Application.Interfaces;
using BitScatter.Domain.Enums;
using BitScatter.Domain.Exceptions;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace BitScatter.Infrastructure.Storage;

public class FileSystemStorageProvider : IStorageProvider
{
    private readonly string _basePath;
    private readonly ILogger<FileSystemStorageProvider> _logger;
    private readonly AsyncRetryPolicy _retryPolicy;

    public StorageProviderType ProviderType => StorageProviderType.FileSystem;

    public FileSystemStorageProvider(string basePath, ILogger<FileSystemStorageProvider> logger)
    {
        _basePath = basePath;
        _logger = logger;
        _retryPolicy = Policy
            .Handle<IOException>()
            .WaitAndRetryAsync(3, attempt => TimeSpan.FromMilliseconds(200 * attempt),
                (ex, delay, attempt, _) =>
                    _logger.LogWarning(ex, "Retry {Attempt} after {Delay}ms for filesystem operation", attempt, delay.TotalMilliseconds));

        Directory.CreateDirectory(_basePath);
    }

    public async Task<string> SaveChunkAsync(Stream data, string key, CancellationToken cancellationToken = default)
    {
        var filePath = GetFilePath(key);
        var dir = Path.GetDirectoryName(filePath)!;
        Directory.CreateDirectory(dir);

        _logger.LogDebug("Saving chunk to filesystem: {FilePath}", filePath);

        await _retryPolicy.ExecuteAsync(async () =>
        {
            await using var fileStream = File.Create(filePath);
            await data.CopyToAsync(fileStream, cancellationToken);
        });

        _logger.LogDebug("Chunk saved to filesystem: {FilePath}", filePath);
        return key;
    }

    public async Task<Stream> ReadChunkAsync(string key, CancellationToken cancellationToken = default)
    {
        var filePath = GetFilePath(key);

        if (!File.Exists(filePath))
            throw new ChunkNotFoundException(key);

        _logger.LogDebug("Reading chunk from filesystem: {FilePath}", filePath);

        var memoryStream = new MemoryStream();
        await _retryPolicy.ExecuteAsync(async () =>
        {
            await using var fileStream = File.OpenRead(filePath);
            await fileStream.CopyToAsync(memoryStream, cancellationToken);
        });

        memoryStream.Seek(0, SeekOrigin.Begin);
        return memoryStream;
    }

    public Task DeleteChunkAsync(string key, CancellationToken cancellationToken = default)
    {
        var filePath = GetFilePath(key);
        if (File.Exists(filePath))
        {
            _logger.LogDebug("Deleting chunk from filesystem: {FilePath}", filePath);
            File.Delete(filePath);
        }
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        var filePath = GetFilePath(key);
        return Task.FromResult(File.Exists(filePath));
    }

    private string GetFilePath(string key)
    {
        var sanitized = key.Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(_basePath, sanitized);
    }
}
