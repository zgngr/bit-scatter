namespace BitScatter.Infrastructure.Configuration;

public class S3ProviderOptions
{
    public string Name { get; set; } = "s3";
    public string Bucket { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string? Endpoint { get; set; }
    public bool? ForcePathStyle { get; set; }
}
