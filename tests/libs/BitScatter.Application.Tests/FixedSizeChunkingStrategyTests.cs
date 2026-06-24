using BitScatter.Application.DTOs;
using BitScatter.Application.Strategies;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace BitScatter.Application.Tests;

public class FixedSizeChunkingStrategyTests
{
    private static FixedSizeChunkingStrategy CreateStrategy(int chunkSize) =>
        new(chunkSize, NullLogger<FixedSizeChunkingStrategy>.Instance);

    [Fact]
    public async Task ChunkAsync_SmallFile_ProducesOneChunk()
    {
        var strategy = CreateStrategy(1024);
        var data = new byte[500];
        Random.Shared.NextBytes(data);

        using var stream = new MemoryStream(data);
        var chunks = await strategy.ChunkAsync(stream).ToListAsync();

        chunks.Should().HaveCount(1);
        chunks[0].Index.Should().Be(0);
        chunks[0].Size.Should().Be(500);
        chunks[0].Sha256Checksum.Should().NotBeNullOrEmpty();

        foreach (var c in chunks) c.Dispose();
    }

    [Fact]
    public async Task ChunkAsync_ExactMultiple_ProducesCorrectChunkCount()
    {
        var strategy = CreateStrategy(100);
        var data = new byte[300];

        using var stream = new MemoryStream(data);
        var chunks = await strategy.ChunkAsync(stream).ToListAsync();

        chunks.Should().HaveCount(3);
        chunks.Select(c => c.Index).Should().BeEquivalentTo([0, 1, 2]);
        chunks.All(c => c.Size == 100).Should().BeTrue();

        foreach (var c in chunks) c.Dispose();
    }

    [Fact]
    public async Task ChunkAsync_NonExactMultiple_LastChunkIsSmaller()
    {
        var strategy = CreateStrategy(100);
        var data = new byte[250];

        using var stream = new MemoryStream(data);
        var chunks = await strategy.ChunkAsync(stream).ToListAsync();

        chunks.Should().HaveCount(3);
        chunks[0].Size.Should().Be(100);
        chunks[1].Size.Should().Be(100);
        chunks[2].Size.Should().Be(50);

        foreach (var c in chunks) c.Dispose();
    }

    [Fact]
    public async Task ChunkAsync_EmptyStream_ProducesNoChunks()
    {
        var strategy = CreateStrategy(1024);
        using var stream = new MemoryStream();

        var chunks = await strategy.ChunkAsync(stream).ToListAsync();

        chunks.Should().BeEmpty();
    }

    [Fact]
    public async Task ChunkAsync_ChecksumsAreConsistent()
    {
        var strategy = CreateStrategy(100);
        var data = new byte[100];
        Random.Shared.NextBytes(data);

        using var stream1 = new MemoryStream(data);
        using var stream2 = new MemoryStream(data);

        var chunks1 = await strategy.ChunkAsync(stream1).ToListAsync();
        var chunks2 = await strategy.ChunkAsync(stream2).ToListAsync();

        chunks1[0].Sha256Checksum.Should().Be(chunks2[0].Sha256Checksum);

        foreach (var c in chunks1.Concat(chunks2)) c.Dispose();
    }

    [Fact]
    public void Constructor_NegativeChunkSize_Throws()
    {
        var act = () => CreateStrategy(-1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_ZeroChunkSize_Throws()
    {
        var act = () => CreateStrategy(0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_ExceedsMaxChunkSize_Throws()
    {
        var act = () => CreateStrategy(UploadOptions.MaxChunkSizeBytes + 1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_ExactlyMaxChunkSize_DoesNotThrow()
    {
        var act = () => CreateStrategy(UploadOptions.MaxChunkSizeBytes);
        act.Should().NotThrow();
    }
}
