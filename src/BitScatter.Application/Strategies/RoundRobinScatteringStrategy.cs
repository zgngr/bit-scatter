using BitScatter.Application.Interfaces;

namespace BitScatter.Application.Strategies;

public class RoundRobinScatteringStrategy : IScatteringStrategy
{
    public IStorageProvider SelectProvider(int chunkIndex, IReadOnlyList<IStorageProvider> providers)
    {
        if (providers.Count == 0)
            throw new InvalidOperationException("No storage providers available for scattering.");

        return providers[chunkIndex % providers.Count];
    }
}
