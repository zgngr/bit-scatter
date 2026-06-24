namespace BitScatter.Application.Interfaces;

public interface IPlacementStrategy
{
    IStorageProvider SelectProvider(int chunkIndex, IReadOnlyList<IStorageProvider> providers);
}
