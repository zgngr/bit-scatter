namespace BitScatter.Application.DTOs;

public class UploadResult
{
    public Guid FileManifestId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public long OriginalSize { get; set; }
    public int ChunkCount { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}
