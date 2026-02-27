using BitScatter.Domain.Entities;

namespace BitScatter.Application.Interfaces;

public interface IFileManifestRepository
{
    Task SaveAsync(FileManifest manifest, CancellationToken cancellationToken = default);

    Task<FileManifest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<FileManifest?> GetByFileNameAsync(string fileName, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FileManifest>> GetAllAsync(CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
