using BitScatter.Application.DTOs;

namespace BitScatter.Application.Interfaces;

public interface IChunkingStrategy
{
    IAsyncEnumerable<ChunkData> ChunkAsync(Stream source, CancellationToken cancellationToken = default);
}
