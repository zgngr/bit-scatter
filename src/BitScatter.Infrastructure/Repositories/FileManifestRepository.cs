using BitScatter.Application.Interfaces;
using BitScatter.Domain.Entities;
using BitScatter.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BitScatter.Infrastructure.Repositories;

public class FileManifestRepository : IFileManifestRepository
{
    private readonly BitScatterDbContext _context;
    private readonly ILogger<FileManifestRepository> _logger;

    public FileManifestRepository(BitScatterDbContext context, ILogger<FileManifestRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task SaveAsync(FileManifest manifest, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Saving file manifest: {Id}", manifest.Id);
        await _context.FileManifests.AddAsync(manifest, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("File manifest saved: {Id} ({FileName})", manifest.Id, manifest.FileName);
    }

    public async Task<FileManifest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Fetching file manifest by ID: {Id}", id);
        return await _context.FileManifests
            .Include(m => m.Chunks)
            .FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
    }

    public async Task<FileManifest?> GetByFileNameAsync(string fileName, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Fetching file manifest by name: {FileName}", fileName);
        return await _context.FileManifests
            .Include(m => m.Chunks)
            .FirstOrDefaultAsync(m => m.FileName == fileName, cancellationToken);
    }

    public async Task<IReadOnlyList<FileManifest>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.FileManifests
            .Include(m => m.Chunks)
            .OrderByDescending(m => m.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Deleting file manifest: {Id}", id);
        var manifest = await _context.FileManifests.FindAsync([id], cancellationToken);
        if (manifest is not null)
        {
            _context.FileManifests.Remove(manifest);
            await _context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("File manifest deleted: {Id}", id);
        }
    }
}
