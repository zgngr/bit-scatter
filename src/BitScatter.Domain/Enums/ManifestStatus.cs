namespace BitScatter.Domain.Enums;

public enum ManifestStatus
{
    /// <summary>Chunks are being uploaded; the manifest is not yet usable.</summary>
    Pending = 0,

    /// <summary>All chunks were saved successfully; the file is available for download.</summary>
    Complete = 1
}
