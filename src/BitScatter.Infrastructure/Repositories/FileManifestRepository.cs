using BitScatter.Application.Interfaces;
using BitScatter.Domain.Entities;
using BitScatter.Domain.Enums;
using BitScatter.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BitScatter.Infrastructure.Repositories;

public class FileManifestRepository : IFileManifestRepository
{
    private readonly IDbContextFactory<BitScatterDbContext> _factory;
    private readonly ILogger<FileManifestRepository> _logger;

    public FileManifestRepository(IDbContextFactory<BitScatterDbContext> factory, ILogger<FileManifestRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task SaveAsync(FileManifest manifest, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Saving file manifest: {Id}", manifest.Id);
        await using var context = await _factory.CreateDbContextAsync(cancellationToken);
        await context.FileManifests.AddAsync(manifest, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("File manifest saved: {Id} ({FileName})", manifest.Id, manifest.FileName);
    }

    public async Task CompleteAsync(Guid id, string sha256Checksum, IReadOnlyList<ChunkInfo> chunks, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Completing file manifest: {Id}", id);
        await using var context = await _factory.CreateDbContextAsync(cancellationToken);

        var manifest = await context.FileManifests.FindAsync([id], cancellationToken);
        if (manifest is null)
            throw new KeyNotFoundException($"File manifest {id} not found during completion.");

        manifest.Sha256Checksum = sha256Checksum;
        manifest.Status = ManifestStatus.Complete;
        await context.ChunkInfos.AddRangeAsync(chunks, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("File manifest completed: {Id} ({FileName})", id, manifest.FileName);
    }

    public async Task<FileManifest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Fetching file manifest by ID: {Id}", id);
        await using var context = await _factory.CreateDbContextAsync(cancellationToken);
        return await context.FileManifests
            .Include(m => m.Chunks)
            .FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
    }

    public async Task<FileManifest?> GetByFileNameAsync(string fileName, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Fetching file manifest by name: {FileName}", fileName);
        await using var context = await _factory.CreateDbContextAsync(cancellationToken);
        return await context.FileManifests
            .Include(m => m.Chunks)
            .FirstOrDefaultAsync(m => m.FileName == fileName, cancellationToken);
    }

    public async Task<IReadOnlyList<FileManifest>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _factory.CreateDbContextAsync(cancellationToken);
        return await context.FileManifests
            .Include(m => m.Chunks)
            .Where(m => m.Status == ManifestStatus.Complete)
            .OrderByDescending(m => m.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Deleting file manifest: {Id}", id);
        await using var context = await _factory.CreateDbContextAsync(cancellationToken);
        var manifest = await context.FileManifests.FindAsync([id], cancellationToken);
        if (manifest is not null)
        {
            context.FileManifests.Remove(manifest);
            await context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("File manifest deleted: {Id}", id);
        }
    }
}
