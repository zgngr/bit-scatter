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

    public string Name { get; }
    public StorageProviderType ProviderType => StorageProviderType.FileSystem;

    public FileSystemStorageProvider(string name, string basePath, ILogger<FileSystemStorageProvider> logger)
    {
        Name = name;
        _logger = logger;
        // Resolve to a canonical absolute path and ensure it ends with a separator so
        // that StartsWith checks cannot be fooled by sibling directories that share a prefix
        // (e.g. /tmp/chunks vs /tmp/chunks-evil).
        _basePath = Path.GetFullPath(basePath).TrimEnd(Path.DirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
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

    public Task<Stream> ReadChunkAsync(string key, CancellationToken cancellationToken = default)
    {
        var filePath = GetFilePath(key);

        if (!File.Exists(filePath))
            throw new ChunkNotFoundException(key);

        _logger.LogDebug("Reading chunk from filesystem: {FilePath}", filePath);

        return Task.FromResult<Stream>(File.OpenRead(filePath));
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
        var fullPath = Path.GetFullPath(Path.Combine(_basePath, sanitized));

        if (!fullPath.StartsWith(_basePath, StringComparison.Ordinal))
            throw new ArgumentException($"Storage key '{key}' resolves outside the storage base path.", nameof(key));

        return fullPath;
    }
}
