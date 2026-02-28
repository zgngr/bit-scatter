namespace BitScatter.Application.DTOs;

public class DeleteResult
{
    public Guid FileManifestId { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}
