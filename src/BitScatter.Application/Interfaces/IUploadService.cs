using BitScatter.Application.DTOs;

namespace BitScatter.Application.Interfaces;

public interface IUploadService
{
    Task<UploadResult> UploadAsync(string filePath, UploadOptions options, IProgress<(int completed, int total)>? progress = null, CancellationToken cancellationToken = default);

    Task<BatchUploadResult> UploadManyAsync(IEnumerable<string> filePaths, UploadOptions options, Func<string, IProgress<(int completed, int total)>?>? progressFactory = null, CancellationToken cancellationToken = default);
}
