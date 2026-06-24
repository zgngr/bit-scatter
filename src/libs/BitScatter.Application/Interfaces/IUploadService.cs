using BitScatter.Application.DTOs;

namespace BitScatter.Application.Interfaces;

public interface IUploadService
{
    Task<BatchUploadResult> UploadManyAsync(IEnumerable<string> filePaths, UploadOptions options, Func<string, IProgress<(int completed, int total)>?>? progressFactory = null, CancellationToken cancellationToken = default);
}
