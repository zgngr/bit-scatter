namespace BitScatter.Application.DTOs;

public class DownloadResult
{
    public string OutputPath { get; set; } = string.Empty;
    public Guid FileManifestId { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}
