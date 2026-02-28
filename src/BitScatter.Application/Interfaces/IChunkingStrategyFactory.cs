namespace BitScatter.Application.Interfaces;

public interface IChunkingStrategyFactory
{
    IChunkingStrategy Create(int chunkSizeBytes);
}
