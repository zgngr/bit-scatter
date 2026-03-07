using BitScatter.Domain.Entities;

namespace BitScatter.Application.Interfaces;

public interface IFileManifestRepository
{
    Task SaveAsync(FileManifest manifest, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically attaches <paramref name="chunks"/> to an existing manifest and marks it
    /// <see cref="Domain.Enums.ManifestStatus.Complete"/>.
    /// Called after all chunk bytes have been successfully written to their storage providers.
    /// </summary>
    Task CompleteAsync(Guid id, string sha256Checksum, IReadOnlyList<ChunkInfo> chunks, CancellationToken cancellationToken = default);

    Task<FileManifest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<FileManifest?> GetByFileNameAsync(string fileName, CancellationToken cancellationToken = default);

    /// <summary>Returns only <see cref="Domain.Enums.ManifestStatus.Complete"/> manifests.</summary>
    Task<IReadOnlyList<FileManifest>> GetAllAsync(CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
