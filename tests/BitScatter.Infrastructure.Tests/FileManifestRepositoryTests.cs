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
    public async Task GetAllAsync_ReturnsAllManifests()
    {
        await _sut.SaveAsync(new FileManifest { FileName = "a.bin", Sha256Checksum = "a" });
        await _sut.SaveAsync(new FileManifest { FileName = "b.bin", Sha256Checksum = "b" });

        var all = await _sut.GetAllAsync();
        all.Should().HaveCount(2);
    }

    private sealed class TestDbContextFactory(DbContextOptions<BitScatterDbContext> options)
        : IDbContextFactory<BitScatterDbContext>
    {
        public BitScatterDbContext CreateDbContext() => new(options);
    }
}
