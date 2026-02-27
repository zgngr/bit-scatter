using BitScatter.Domain.Entities;
using BitScatter.Domain.Enums;
using BitScatter.Domain.Exceptions;
using FluentAssertions;

namespace BitScatter.Domain.Tests;

public class ChunkInfoTests
{
    [Fact]
    public void ChunkInfo_DefaultId_IsNotEmpty()
    {
        var chunk = new ChunkInfo();
        chunk.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void ChunkInfo_Properties_SetCorrectly()
    {
        var manifestId = Guid.NewGuid();
        var chunk = new ChunkInfo
        {
            FileManifestId = manifestId,
            ChunkIndex = 3,
            Size = 512,
            Sha256Checksum = "abc",
            StorageProviderType = StorageProviderType.Database,
            StorageKey = "key/3"
        };

        chunk.FileManifestId.Should().Be(manifestId);
        chunk.ChunkIndex.Should().Be(3);
        chunk.Size.Should().Be(512);
        chunk.StorageProviderType.Should().Be(StorageProviderType.Database);
    }
}

public class ExceptionTests
{
    [Fact]
    public void ChecksumMismatchException_ContainsDetails()
    {
        var ex = new ChecksumMismatchException("expected123", "actual456");

        ex.Expected.Should().Be("expected123");
        ex.Actual.Should().Be("actual456");
        ex.Message.Should().Contain("expected123").And.Contain("actual456");
    }

    [Fact]
    public void ChunkNotFoundException_ContainsStorageKey()
    {
        var ex = new ChunkNotFoundException("my/key/0");

        ex.StorageKey.Should().Be("my/key/0");
        ex.Message.Should().Contain("my/key/0");
    }
}
