using BitScatter.Application.DTOs;

namespace BitScatter.Application.Interfaces;

public interface IUploadService
{
    Task<UploadResult> UploadAsync(string filePath, UploadOptions options, CancellationToken cancellationToken = default);
}
