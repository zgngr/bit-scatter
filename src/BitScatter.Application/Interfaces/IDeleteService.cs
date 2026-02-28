using BitScatter.Application.DTOs;

namespace BitScatter.Application.Interfaces;

public interface IDeleteService
{
    Task<DeleteResult> DeleteAsync(Guid fileManifestId, CancellationToken cancellationToken = default);
}
