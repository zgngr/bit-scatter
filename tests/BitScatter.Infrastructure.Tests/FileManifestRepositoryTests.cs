using BitScatter.Domain.Entities;
using BitScatter.Domain.Enums;
using BitScatter.Infrastructure.Data;
using BitScatter.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BitScatter.Infrastructure.Tests;

public class FileManifestRepositoryTests
{
    private readonly FileManifestRepository _sut;

    public FileManifestRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<BitScatterDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _sut = new FileManifestRepository(new TestDbContextFactory(options), NullLogger<FileManifestRepository>.Instance);
    }

    [Fact]
    public async Task SaveAsync_PersistsManifest()
    {
        var manifest = new FileManifest
        {
            FileName = "test.bin",
            OriginalSize = 1024,
            Sha256Checksum = "abc",
            ChunkSize = 512
        };

        await _sut.SaveAsync(manifest);

        var stored = await _sut.GetByIdAsync(manifest.Id);
        stored.Should().NotBeNull();
        stored!.FileName.Should().Be("test.bin");
    }

    [Fact]
    public async Task GetByIdAsync_NotFound_ReturnsNull()
    {
        var result = await _sut.GetByIdAsync(Guid.NewGuid());
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByFileNameAsync_ReturnsCorrectManifest()
    {
        var manifest = new FileManifest { FileName = "unique.bin", Sha256Checksum = "x" };
        await _sut.SaveAsync(manifest);

        var found = await _sut.GetByFileNameAsync("unique.bin");
        found.Should().NotBeNull();
        found!.Id.Should().Be(manifest.Id);
    }

    [Fact]
    public async Task GetByIdAsync_IncludesChunks()
    {
        var manifest = new FileManifest { FileName = "chunked.bin", Sha256Checksum = "y" };
        manifest.Chunks.Add(new ChunkInfo
        {
            FileManifestId = manifest.Id,
            ChunkIndex = 0,
            Size = 100,
            Sha256Checksum = "chk",
            StorageProviderType = StorageProviderType.FileSystem,
            StorageKey = $"{manifest.Id}/0"
        });
        await _sut.SaveAsync(manifest);

        var found = await _sut.GetByIdAsync(manifest.Id);
        found!.Chunks.Should().HaveCount(1);
        found.Chunks[0].ChunkIndex.Should().Be(0);
    }

    [Fact]
    public async Task DeleteAsync_RemovesManifest()
    {
        var manifest = new FileManifest { FileName = "todelete.bin", Sha256Checksum = "z" };
        await _sut.SaveAsync(manifest);

        await _sut.DeleteAsync(manifest.Id);

        var result = await _sut.GetByIdAsync(manifest.Id);
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAllAsync_ReturnsOnlyCompleteManifests()
    {
        var complete = new FileManifest { FileName = "a.bin", Sha256Checksum = "a" };
        var pending = new FileManifest { FileName = "b.bin", Sha256Checksum = "b" };

        await _sut.SaveAsync(complete);
        await _sut.SaveAsync(pending);
        // Only complete is finalised; pending stays Pending
        await _sut.CompleteAsync(complete.Id, []);

        var all = await _sut.GetAllAsync();
        all.Should().HaveCount(1);
        all[0].Id.Should().Be(complete.Id);
    }

    [Fact]
    public async Task GetAllAsync_ExcludesPendingManifests()
    {
        var manifest = new FileManifest { FileName = "pending.bin", Sha256Checksum = "p" };
        await _sut.SaveAsync(manifest);  // stays Pending

        var all = await _sut.GetAllAsync();
        all.Should().BeEmpty();
    }

    [Fact]
    public async Task CompleteAsync_SetsStatusToCompleteAndPersistsChunks()
    {
        var manifest = new FileManifest { FileName = "complete.bin", Sha256Checksum = "c" };
        await _sut.SaveAsync(manifest);

        var chunks = new List<ChunkInfo>
        {
            new()
            {
                FileManifestId = manifest.Id,
                ChunkIndex = 0,
                Size = 512,
                Sha256Checksum = "chk0",
                StorageProviderType = StorageProviderType.FileSystem,
                StorageKey = $"{manifest.Id}/0"
            }
        };

        await _sut.CompleteAsync(manifest.Id, chunks);

        var found = await _sut.GetByIdAsync(manifest.Id);
        found.Should().NotBeNull();
        found!.Status.Should().Be(ManifestStatus.Complete);
        found.Chunks.Should().HaveCount(1);
        found.Chunks[0].ChunkIndex.Should().Be(0);
    }

    [Fact]
    public async Task CompleteAsync_ManifestNotFound_ThrowsKeyNotFoundException()
    {
        var act = () => _sut.CompleteAsync(Guid.NewGuid(), []);
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task CompleteAsync_WithNoChunks_SetsStatusToComplete()
    {
        var manifest = new FileManifest { FileName = "empty.bin", Sha256Checksum = "e" };
        await _sut.SaveAsync(manifest);

        await _sut.CompleteAsync(manifest.Id, []);

        var found = await _sut.GetByIdAsync(manifest.Id);
        found!.Status.Should().Be(ManifestStatus.Complete);
        found.Chunks.Should().BeEmpty();
    }

    private sealed class TestDbContextFactory(DbContextOptions<BitScatterDbContext> options)
        : IDbContextFactory<BitScatterDbContext>
    {
        public BitScatterDbContext CreateDbContext() => new(options);
    }
}
