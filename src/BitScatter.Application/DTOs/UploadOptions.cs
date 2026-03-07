using BitScatter.Domain.Enums;

namespace BitScatter.Application.DTOs;

public class UploadOptions
{
    /// <summary>
    /// Maximum allowed chunk size (512 MB). Values above this cause a 2 GB+ allocation per chunk.
    /// </summary>
    public const int MaxChunkSizeBytes = 512 * 1024 * 1024;

    private int _chunkSizeBytes = 1024 * 1024; // 1 MB default

    public int ChunkSizeBytes
    {
        get => _chunkSizeBytes;
        set
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), "Chunk size must be positive.");
            if (value > MaxChunkSizeBytes)
                throw new ArgumentOutOfRangeException(nameof(value),
                    $"Chunk size must not exceed {MaxChunkSizeBytes} bytes (512 MB).");
            _chunkSizeBytes = value;
        }
    }

    /// <summary>
    /// Storage providers to scatter chunks across. Null or empty means all available registered providers.
    /// </summary>
    public StorageProviderType[]? StorageProviders { get; set; } = null;

    /// <summary>
    /// Maximum number of files that may be uploaded concurrently in <c>UploadManyAsync</c>.
    /// Defaults to 4 — a conservative cap that limits memory and I/O pressure regardless of CPU count.
    /// </summary>
    public int MaxConcurrentUploads { get; set; } = 4;

    private int _maxInFlightChunks = 8;

    /// <summary>
    /// Maximum number of chunk writes that may be in-flight concurrently for a single file upload.
    /// </summary>
    public int MaxInFlightChunks
    {
        get => _maxInFlightChunks;
        set
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), "Max in-flight chunks must be positive.");
            _maxInFlightChunks = value;
        }
    }
}
