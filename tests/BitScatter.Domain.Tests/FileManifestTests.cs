using BitScatter.Domain.Entities;
using BitScatter.Domain.Enums;
using FluentAssertions;

namespace BitScatter.Domain.Tests;

public class FileManifestTests
{
    [Fact]
    public void FileManifest_DefaultValues_AreCorrect()
    {
        var manifest = new FileManifest();

        manifest.Id.Should().NotBe(Guid.Empty);
        manifest.FileName.Should().BeEmpty();
        manifest.Chunks.Should().BeEmpty();
        manifest.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void FileManifest_CanAddChunks()
    {
        var manifest = new FileManifest { FileName = "test.bin" };
        var chunk = new ChunkInfo
        {
            FileManifestId = manifest.Id,
            ChunkIndex = 0,
            Size = 1024,
            Sha256Checksum = "abc123",
            StorageProviderType = StorageProviderType.FileSystem,
            StorageKey = $"{manifest.Id}/0"
        };

        manifest.Chunks.Add(chunk);

        manifest.Chunks.Should().HaveCount(1);
        manifest.Chunks[0].ChunkIndex.Should().Be(0);
    }

    [Fact]
    public void FileManifest_WithProperties_SetsCorrectly()
    {
        var id = Guid.NewGuid();
        var manifest = new FileManifest
        {
            Id = id,
            FileName = "largefile.bin",
            OriginalSize = 100_000_000,
            Sha256Checksum = "deadbeef",
            ChunkSize = 1_048_576
        };

        manifest.Id.Should().Be(id);
        manifest.FileName.Should().Be("largefile.bin");
        manifest.OriginalSize.Should().Be(100_000_000);
        manifest.ChunkSize.Should().Be(1_048_576);
    }
}
