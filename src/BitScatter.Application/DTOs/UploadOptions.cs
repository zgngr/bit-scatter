using BitScatter.Domain.Enums;

namespace BitScatter.Application.DTOs;

public class UploadOptions
{
    public int ChunkSizeBytes { get; set; } = 1024 * 1024; // 1 MB default
    /// <summary>
    /// Storage providers to scatter chunks across. Null or empty means all available registered providers.
    /// </summary>
    public StorageProviderType[]? StorageProviders { get; set; } = null;
}
