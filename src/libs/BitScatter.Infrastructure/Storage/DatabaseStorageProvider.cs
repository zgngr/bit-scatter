using BitScatter.Application.Interfaces;
using BitScatter.Domain.Enums;
using BitScatter.Domain.Exceptions;
using BitScatter.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace BitScatter.Infrastructure.Storage;

public class DatabaseStorageProvider : IStorageProvider
{
    private readonly IDbContextFactory<ChunkStorageDbContext> _factory;
    private readonly ILogger<DatabaseStorageProvider> _logger;
    private readonly AsyncRetryPolicy _retryPolicy;
    private readonly string _name;

    public string Name => _name;
    public StorageProviderType ProviderType => StorageProviderType.Database;

    public DatabaseStorageProvider(
        IDbContextFactory<ChunkStorageDbContext> factory,
        ILogger<DatabaseStorageProvider> logger,
        string? name = null)
    {
        _factory = factory;
        _logger = logger;
        _name = string.IsNullOrWhiteSpace(name) ? "database" : name;
        _retryPolicy = Policy
            .Handle<Exception>(ex => ex is not OperationCanceledException)
            .WaitAndRetryAsync(3, attempt => TimeSpan.FromMilliseconds(200 * attempt),
                (ex, delay, attempt, _) =>
                    _logger.LogWarning(ex, "Retry {Attempt} after {Delay}ms for DB operation", attempt, delay.TotalMilliseconds));
    }

    public async Task<string> SaveChunkAsync(Stream data, string key, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Saving chunk to database with key: {Key}", key);

        // Avoid double allocation: if the stream is already a MemoryStream (the common case from
        // FixedSizeChunkingStrategy), call ToArray() directly instead of copying into another buffer first.
        // For other seekable streams, pre-size the MemoryStream to the known length to avoid growth copies.
        byte[] bytes;
        if (data is MemoryStream existingMs)
        {
            bytes = existingMs.ToArray();
        }
        else
        {
            var capacity = data.CanSeek ? (int)(data.Length - data.Position) : 0;
            using var memStream = capacity > 0 ? new MemoryStream(capacity) : new MemoryStream();
            await data.CopyToAsync(memStream, cancellationToken);
            bytes = memStream.ToArray();
        }

        await _retryPolicy.ExecuteAsync(async () =>
        {
            await using var context = await _factory.CreateDbContextAsync(cancellationToken);

            var existing = await context.ChunkStorages
                .FirstOrDefaultAsync(c => c.StorageKey == key, cancellationToken);

            if (existing is not null)
            {
                existing.Data = bytes;
                context.ChunkStorages.Update(existing);
            }
            else
            {
                await context.ChunkStorages.AddAsync(new ChunkStorage
                {
                    StorageKey = key,
                    Data = bytes
                }, cancellationToken);
            }

            await context.SaveChangesAsync(cancellationToken);
        });

        _logger.LogDebug("Chunk saved to database: {Key}", key);
        return key;
    }

    public async Task<Stream> ReadChunkAsync(string key, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Reading chunk from database with key: {Key}", key);

        var chunk = await _retryPolicy.ExecuteAsync(async () =>
        {
            await using var context = await _factory.CreateDbContextAsync(cancellationToken);
            return await context.ChunkStorages
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.StorageKey == key, cancellationToken);
        });

        if (chunk is null)
            throw new ChunkNotFoundException(key);

        return new MemoryStream(chunk.Data);
    }

    public async Task DeleteChunkAsync(string key, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Deleting chunk from database: {Key}", key);

        await _retryPolicy.ExecuteAsync(async () =>
        {
            await using var context = await _factory.CreateDbContextAsync(cancellationToken);
            var chunk = await context.ChunkStorages
                .FirstOrDefaultAsync(c => c.StorageKey == key, cancellationToken);

            if (chunk is not null)
            {
                context.ChunkStorages.Remove(chunk);
                await context.SaveChangesAsync(cancellationToken);
            }
        });
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var context = await _factory.CreateDbContextAsync(cancellationToken);
        return await context.ChunkStorages
            .AnyAsync(c => c.StorageKey == key, cancellationToken);
    }
}
