using BitScatter.Domain.Exceptions;
using BitScatter.Infrastructure.Data;
using BitScatter.Infrastructure.Storage;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BitScatter.Infrastructure.Tests;

public class DatabaseStorageProviderTests
{
    private static IDbContextFactory<ChunkStorageDbContext> CreateFactory(string dbName)
    {
        var options = new DbContextOptionsBuilder<ChunkStorageDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new InMemoryDbContextFactory(options);
    }

    private static DatabaseStorageProvider CreateSut(IDbContextFactory<ChunkStorageDbContext> factory)
        => new(factory, NullLogger<DatabaseStorageProvider>.Instance);

    [Fact]
    public async Task SaveChunkAsync_PersistsData()
    {
        var sut = CreateSut(CreateFactory(Guid.NewGuid().ToString()));
        using var data = new MemoryStream([1, 2, 3]);

        await sut.SaveChunkAsync(data, "key1");

        (await sut.ExistsAsync("key1")).Should().BeTrue();
    }

    [Fact]
    public async Task ReadChunkAsync_ReturnsCorrectData()
    {
        var sut = CreateSut(CreateFactory(Guid.NewGuid().ToString()));
        var bytes = new byte[] { 10, 20, 30 };
        using var write = new MemoryStream(bytes);
        await sut.SaveChunkAsync(write, "read-key");

        await using var result = await sut.ReadChunkAsync("read-key");
        var buf = new byte[3];
        await result.ReadExactlyAsync(buf);

        buf.Should().BeEquivalentTo(bytes);
    }

    [Fact]
    public async Task ReadChunkAsync_MissingKey_ThrowsChunkNotFoundException()
    {
        var sut = CreateSut(CreateFactory(Guid.NewGuid().ToString()));

        var act = () => sut.ReadChunkAsync("nonexistent");
        await act.Should().ThrowAsync<ChunkNotFoundException>();
    }

    [Fact]
    public async Task DeleteChunkAsync_ExistingKey_RemovesChunk()
    {
        var sut = CreateSut(CreateFactory(Guid.NewGuid().ToString()));
        using var data = new MemoryStream([1, 2, 3]);
        await sut.SaveChunkAsync(data, "del-key");

        await sut.DeleteChunkAsync("del-key");

        (await sut.ExistsAsync("del-key")).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteChunkAsync_MissingKey_DoesNotThrow()
    {
        var sut = CreateSut(CreateFactory(Guid.NewGuid().ToString()));

        var act = () => sut.DeleteChunkAsync("nonexistent");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeleteChunkAsync_TransientFailure_RetriesAndSucceeds()
    {
        var dbName = Guid.NewGuid().ToString();
        var inner = CreateFactory(dbName);
        var faulting = new FaultingDbContextFactory(inner, failCount: 1);

        // Populate via the non-faulting factory
        var prep = CreateSut(inner);
        using var data = new MemoryStream([1, 2, 3]);
        await prep.SaveChunkAsync(data, "retry-key");

        // Delete through the faulting factory — first call throws, retry succeeds
        var sut = CreateSut(faulting);
        await sut.DeleteChunkAsync("retry-key");

        (await prep.ExistsAsync("retry-key")).Should().BeFalse();
    }

    private sealed class InMemoryDbContextFactory(DbContextOptions<ChunkStorageDbContext> options)
        : IDbContextFactory<ChunkStorageDbContext>
    {
        public ChunkStorageDbContext CreateDbContext() => new(options);
    }

    private sealed class FaultingDbContextFactory(
        IDbContextFactory<ChunkStorageDbContext> inner, int failCount)
        : IDbContextFactory<ChunkStorageDbContext>
    {
        private int _failuresRemaining = failCount;

        public ChunkStorageDbContext CreateDbContext()
        {
            if (_failuresRemaining-- > 0)
                throw new InvalidOperationException("Simulated transient failure");
            return inner.CreateDbContext();
        }
    }
}
