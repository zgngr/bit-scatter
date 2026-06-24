namespace BitScatter.Application.DTOs;

public class BatchUploadResult
{
    public IReadOnlyList<UploadResult> Results { get; init; } = [];

    public int TotalCount => Results.Count;
    public int SuccessCount => Results.Count(r => r.Success);
    public int FailureCount => Results.Count(r => !r.Success);
    public bool AllSucceeded => FailureCount == 0;
}
