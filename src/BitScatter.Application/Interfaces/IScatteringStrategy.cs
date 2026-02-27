namespace BitScatter.Application.Interfaces;

public interface IScatteringStrategy
{
    IStorageProvider SelectProvider(int chunkIndex, IReadOnlyList<IStorageProvider> providers);
}
