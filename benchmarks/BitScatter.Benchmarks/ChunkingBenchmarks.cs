using BenchmarkDotNet.Attributes;
using BitScatter.Application.Strategies;
using Microsoft.Extensions.Logging.Abstractions;

namespace BitScatter.Benchmarks;

[MemoryDiagnoser]
[SimpleJob]
public class ChunkingBenchmarks
{
    private byte[] _data = [];

    [Params(1024 * 1024, 10 * 1024 * 1024)]
    public int FileSizeBytes { get; set; }

    [Params(64 * 1024, 512 * 1024, 1024 * 1024)]
    public int ChunkSizeBytes { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _data = new byte[FileSizeBytes];
        Random.Shared.NextBytes(_data);
    }

    [Benchmark]
    public async Task<int> ChunkFile()
    {
        var strategy = new FixedSizeChunkingStrategy(ChunkSizeBytes, NullLogger<FixedSizeChunkingStrategy>.Instance);
        using var stream = new MemoryStream(_data);
        int count = 0;

        await foreach (var chunk in strategy.ChunkAsync(stream))
        {
            using (chunk)
                count++;
        }

        return count;
    }
}
