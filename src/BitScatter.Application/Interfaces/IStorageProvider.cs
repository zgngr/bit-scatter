using BitScatter.Domain.Enums;

namespace BitScatter.Application.Interfaces;

public interface IStorageProvider
{
    string Name { get; }

    StorageProviderType ProviderType { get; }

    Task<string> SaveChunkAsync(Stream data, string key, CancellationToken cancellationToken = default);

    Task<Stream> ReadChunkAsync(string key, CancellationToken cancellationToken = default);

    Task DeleteChunkAsync(string key, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);
}
