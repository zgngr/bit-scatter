using BitScatter.Domain.Enums;

namespace BitScatter.Domain.Entities;

public class FileManifest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FileName { get; set; } = string.Empty;
    public long OriginalSize { get; set; }
    public string Sha256Checksum { get; set; } = string.Empty;
    public int ChunkSize { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Tracks whether the upload completed successfully.
    /// A <see cref="ManifestStatus.Pending"/> manifest has chunks in storage but is not yet
    /// fully recorded; it is excluded from listings and may be reclaimed by a cleanup process.
    /// </summary>
    public ManifestStatus Status { get; set; } = ManifestStatus.Pending;

    public List<ChunkInfo> Chunks { get; set; } = new();
}
