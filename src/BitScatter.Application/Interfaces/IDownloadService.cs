using BitScatter.Application.DTOs;

namespace BitScatter.Application.Interfaces;

public interface IDownloadService
{
    Task<DownloadResult> DownloadAsync(Guid fileManifestId, string outputPath, CancellationToken cancellationToken = default);
}
