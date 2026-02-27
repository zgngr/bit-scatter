using BitScatter.Domain.Enums;

namespace BitScatter.Application.DTOs;

public class UploadOptions
{
    public int ChunkSizeBytes { get; set; } = 1024 * 1024; // 1 MB default
    public StorageProviderType[] StorageProviders { get; set; } = [StorageProviderType.FileSystem];
}
