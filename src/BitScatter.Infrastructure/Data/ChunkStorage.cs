namespace BitScatter.Infrastructure.Data;

public class ChunkStorage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string StorageKey { get; set; } = string.Empty;
    public byte[] Data { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
