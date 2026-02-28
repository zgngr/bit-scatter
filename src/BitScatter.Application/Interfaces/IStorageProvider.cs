using BitScatter.Domain.Enums;

namespace BitScatter.Application.Interfaces;

public interface IStorageProvider
{
    string Name { get; }

    StorageProviderType ProviderType { get; }

    Task<string> SaveChunkAsync(Stream data, string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a stored chunk and returns it as a forward-only readable stream.
    /// </summary>
    /// <remarks>
    /// Callers must treat the returned stream as forward-only (no seekability guarantee).
    /// The caller is responsible for disposing the stream after use.
    /// </remarks>
    Task<Stream> ReadChunkAsync(string key, CancellationToken cancellationToken = default);

    Task DeleteChunkAsync(string key, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);
}
