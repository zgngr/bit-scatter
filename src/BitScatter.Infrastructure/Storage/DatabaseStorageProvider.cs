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
    private readonly ChunkStorageDbContext _context;
    private readonly ILogger<DatabaseStorageProvider> _logger;
    private readonly AsyncRetryPolicy _retryPolicy;

    public string Name => "database";
    public StorageProviderType ProviderType => StorageProviderType.Database;

    public DatabaseStorageProvider(ChunkStorageDbContext context, ILogger<DatabaseStorageProvider> logger)
    {
        _context = context;
        _logger = logger;
        _retryPolicy = Policy
            .Handle<Exception>(ex => ex is not OperationCanceledException)
            .WaitAndRetryAsync(3, attempt => TimeSpan.FromMilliseconds(200 * attempt),
                (ex, delay, attempt, _) =>
                    _logger.LogWarning(ex, "Retry {Attempt} after {Delay}ms for DB operation", attempt, delay.TotalMilliseconds));
    }

    public async Task<string> SaveChunkAsync(Stream data, string key, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Saving chunk to database with key: {Key}", key);

        using var memStream = new MemoryStream();
        await data.CopyToAsync(memStream, cancellationToken);
        var bytes = memStream.ToArray();

        await _retryPolicy.ExecuteAsync(async () =>
        {
            var existing = await _context.ChunkStorages
                .FirstOrDefaultAsync(c => c.StorageKey == key, cancellationToken);

            if (existing is not null)
            {
                existing.Data = bytes;
                _context.ChunkStorages.Update(existing);
            }
            else
            {
                await _context.ChunkStorages.AddAsync(new ChunkStorage
                {
                    StorageKey = key,
                    Data = bytes
                }, cancellationToken);
            }

            await _context.SaveChangesAsync(cancellationToken);
        });

        _logger.LogDebug("Chunk saved to database: {Key}", key);
        return key;
    }

    public async Task<Stream> ReadChunkAsync(string key, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Reading chunk from database with key: {Key}", key);

        var chunk = await _retryPolicy.ExecuteAsync(() =>
            _context.ChunkStorages
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.StorageKey == key, cancellationToken)!);

        if (chunk is null)
            throw new ChunkNotFoundException(key);

        return new MemoryStream(chunk.Data);
    }

    public async Task DeleteChunkAsync(string key, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Deleting chunk from database: {Key}", key);
        var chunk = await _context.ChunkStorages
            .FirstOrDefaultAsync(c => c.StorageKey == key, cancellationToken);

        if (chunk is not null)
        {
            _context.ChunkStorages.Remove(chunk);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        return await _context.ChunkStorages
            .AnyAsync(c => c.StorageKey == key, cancellationToken);
    }
}
