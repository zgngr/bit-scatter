using BitScatter.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace BitScatter.Application.Strategies;

public class FixedSizeChunkingStrategyFactory : IChunkingStrategyFactory
{
    private readonly ILogger<FixedSizeChunkingStrategy> _logger;

    public FixedSizeChunkingStrategyFactory(ILogger<FixedSizeChunkingStrategy> logger)
    {
        _logger = logger;
    }

    public IChunkingStrategy Create(int chunkSizeBytes) =>
        new FixedSizeChunkingStrategy(chunkSizeBytes, _logger);
}
