using BitScatter.Domain.Enums;

namespace BitScatter.Domain.Entities;

public class ChunkInfo
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FileManifestId { get; set; }
    public int ChunkIndex { get; set; }
    public long Size { get; set; }
    public string Sha256Checksum { get; set; } = string.Empty;
    public StorageProviderType StorageProviderType { get; set; }
    public string StorageKey { get; set; } = string.Empty;

    public FileManifest FileManifest { get; set; } = null!;
}
