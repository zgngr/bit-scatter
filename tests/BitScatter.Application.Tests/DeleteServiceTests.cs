using BitScatter.Application.Interfaces;
using BitScatter.Application.Services;
using BitScatter.Domain.Entities;
using BitScatter.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BitScatter.Application.Tests;

public class DeleteServiceTests
{
    private readonly Mock<IFileManifestRepository> _repoMock;
    private readonly Mock<IStorageProvider> _providerMock;
    private readonly DeleteService _sut;

    public DeleteServiceTests()
    {
        _repoMock = new Mock<IFileManifestRepository>();
        _providerMock = new Mock<IStorageProvider>();

        _providerMock.SetupGet(p => p.ProviderType).Returns(StorageProviderType.FileSystem);
        _providerMock.SetupGet(p => p.Name).Returns("node1");

        _sut = new DeleteService(
            [_providerMock.Object],
            _repoMock.Object,
            NullLogger<DeleteService>.Instance);
    }

    private static FileManifest CreateManifest()
    {
        var manifest = new FileManifest
        {
            FileName = "test.bin",
            OriginalSize = 8,
            Sha256Checksum = "abc123",
            ChunkSize = 4
        };

        manifest.Chunks.Add(new ChunkInfo
        {
            FileManifestId = manifest.Id,
            ChunkIndex = 0,
            Size = 4,
            Sha256Checksum = "chunk0hash",
            StorageProviderType = StorageProviderType.FileSystem,
            ProviderName = "node1",
            StorageKey = $"{manifest.Id}/0"
        });
        manifest.Chunks.Add(new ChunkInfo
        {
            FileManifestId = manifest.Id,
            ChunkIndex = 1,
            Size = 4,
            Sha256Checksum = "chunk1hash",
            StorageProviderType = StorageProviderType.FileSystem,
            ProviderName = "node1",
            StorageKey = $"{manifest.Id}/1"
        });

        return manifest;
    }

    [Fact]
    public async Task DeleteAsync_ValidManifest_DeletesAllChunksAndManifest()
    {
        var manifest = CreateManifest();
        _repoMock.Setup(r => r.GetByIdAsync(manifest.Id, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(manifest);

        var result = await _sut.DeleteAsync(manifest.Id);

        result.Success.Should().BeTrue();
        result.FileManifestId.Should().Be(manifest.Id);
        result.ErrorMessage.Should().BeNull();

        _providerMock.Verify(p => p.DeleteChunkAsync($"{manifest.Id}/0", It.IsAny<CancellationToken>()), Times.Once);
        _providerMock.Verify(p => p.DeleteChunkAsync($"{manifest.Id}/1", It.IsAny<CancellationToken>()), Times.Once);
        _repoMock.Verify(r => r.DeleteAsync(manifest.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_ManifestNotFound_ReturnsFailure()
    {
        var id = Guid.NewGuid();
        _repoMock.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
                 .ReturnsAsync((FileManifest?)null);

        var result = await _sut.DeleteAsync(id);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain(id.ToString());
        _repoMock.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_ProviderThrows_ReturnsFailure()
    {
        var manifest = CreateManifest();
        _repoMock.Setup(r => r.GetByIdAsync(manifest.Id, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(manifest);
        _providerMock.Setup(p => p.DeleteChunkAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                     .ThrowsAsync(new IOException("Storage unavailable"));

        var result = await _sut.DeleteAsync(manifest.Id);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Storage unavailable");
    }

    [Fact]
    public async Task DeleteAsync_NoProviderForChunk_ReturnsFailure()
    {
        var manifest = CreateManifest();
        manifest.Chunks[0].ProviderName = "unknown-node";
        manifest.Chunks[0].StorageProviderType = StorageProviderType.Database;

        _repoMock.Setup(r => r.GetByIdAsync(manifest.Id, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(manifest);

        var result = await _sut.DeleteAsync(manifest.Id);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("No storage provider registered");
    }

    [Fact]
    public async Task DeleteAsync_RepositoryDeleteThrows_ReturnsFailure()
    {
        var manifest = CreateManifest();
        _repoMock.Setup(r => r.GetByIdAsync(manifest.Id, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(manifest);
        _repoMock.Setup(r => r.DeleteAsync(manifest.Id, It.IsAny<CancellationToken>()))
                 .ThrowsAsync(new InvalidOperationException("DB error"));

        var result = await _sut.DeleteAsync(manifest.Id);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("DB error");
    }

    [Fact]
    public async Task DeleteAsync_Cancellation_PropagatesAndReturnsFailure()
    {
        var manifest = CreateManifest();
        var cts = new CancellationTokenSource();
        _repoMock.Setup(r => r.GetByIdAsync(manifest.Id, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(manifest);
        _providerMock.Setup(p => p.DeleteChunkAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                     .Returns((string _, CancellationToken ct) =>
                     {
                         ct.ThrowIfCancellationRequested();
                         return Task.CompletedTask;
                     });

        cts.Cancel();
        var result = await _sut.DeleteAsync(manifest.Id, cts.Token);

        result.Success.Should().BeFalse();
    }
}
