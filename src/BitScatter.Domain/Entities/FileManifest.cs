namespace BitScatter.Domain.Entities;

public class FileManifest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FileName { get; set; } = string.Empty;
    public long OriginalSize { get; set; }
    public string Sha256Checksum { get; set; } = string.Empty;
    public int ChunkSize { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<ChunkInfo> Chunks { get; set; } = new();
}
