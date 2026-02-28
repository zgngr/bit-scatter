using System.Security.Cryptography;
using BitScatter.Application.Interfaces;
using BitScatter.Application.Services;
using BitScatter.Domain.Entities;
using BitScatter.Domain.Enums;
using BitScatter.Domain.Exceptions;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BitScatter.Application.Tests;

public class DownloadServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly Mock<IFileManifestRepository> _repoMock;
    private readonly Mock<IStorageProvider> _providerMock;
    private readonly DownloadService _sut;

    private static readonly byte[] Chunk0 = [1, 2, 3, 4];
    private static readonly byte[] Chunk1 = [5, 6, 7, 8];
    private static readonly string Chunk0Checksum = Convert.ToHexString(SHA256.HashData(Chunk0)).ToLowerInvariant();
    private static readonly string Chunk1Checksum = Convert.ToHexString(SHA256.HashData(Chunk1)).ToLowerInvariant();
    private static readonly string FileChecksum = Convert.ToHexString(SHA256.HashData([1, 2, 3, 4, 5, 6, 7, 8])).ToLowerInvariant();

    public DownloadServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);

        _repoMock = new Mock<IFileManifestRepository>();
        _providerMock = new Mock<IStorageProvider>();

        _providerMock.SetupGet(p => p.ProviderType).Returns(StorageProviderType.FileSystem);

        _sut = new DownloadService(
            [_providerMock.Object],
            _repoMock.Object,
            NullLogger<DownloadService>.Instance);
    }

    private FileManifest CreateManifest()
    {
        var manifest = new FileManifest
        {
            FileName = "test.bin",
            OriginalSize = 8,
            Sha256Checksum = FileChecksum,
            ChunkSize = 4
        };

        manifest.Chunks.Add(new ChunkInfo
        {
            FileManifestId = manifest.Id,
            ChunkIndex = 0,
            Size = 4,
            Sha256Checksum = Chunk0Checksum,
            StorageProviderType = StorageProviderType.FileSystem,
            StorageKey = $"{manifest.Id}/0"
        });
        manifest.Chunks.Add(new ChunkInfo
        {
            FileManifestId = manifest.Id,
            ChunkIndex = 1,
            Size = 4,
            Sha256Checksum = Chunk1Checksum,
            StorageProviderType = StorageProviderType.FileSystem,
            StorageKey = $"{manifest.Id}/1"
        });

        return manifest;
    }

    [Fact]
    public async Task DownloadAsync_ValidManifest_ReturnsSuccess()
    {
        var manifest = CreateManifest();
        _repoMock.Setup(r => r.GetByIdAsync(manifest.Id, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(manifest);
        _providerMock
            .Setup(p => p.ReadChunkAsync($"{manifest.Id}/0", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream(Chunk0));
        _providerMock
            .Setup(p => p.ReadChunkAsync($"{manifest.Id}/1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream(Chunk1));

        var outputPath = Path.Combine(_tempDir, "output.bin");
        var result = await _sut.DownloadAsync(manifest.Id, outputPath);

        result.Success.Should().BeTrue();
        result.OutputPath.Should().Be(outputPath);
        File.Exists(outputPath).Should().BeTrue();
    }

    [Fact]
    public async Task DownloadAsync_ManifestNotFound_Throws()
    {
        var id = Guid.NewGuid();
        _repoMock.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
                 .ReturnsAsync((FileManifest?)null);

        var act = () => _sut.DownloadAsync(id, Path.Combine(_tempDir, "out.bin"));
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task DownloadAsync_ChunkChecksumMismatch_Throws()
    {
        var manifest = CreateManifest();
        manifest.Chunks[0].Sha256Checksum = "intentionally-wrong-checksum";

        _repoMock.Setup(r => r.GetByIdAsync(manifest.Id, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(manifest);

        _providerMock
            .Setup(p => p.ReadChunkAsync($"{manifest.Id}/0", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream(Chunk0));

        var outputPath = Path.Combine(_tempDir, "out_bad.bin");
        var act = () => _sut.DownloadAsync(manifest.Id, outputPath);
        await act.Should().ThrowAsync<ChecksumMismatchException>();
    }

    [Fact]
    public async Task DownloadAsync_ChunkChecksumMismatch_DeletesPartialOutputFile()
    {
        var manifest = CreateManifest();
        manifest.Chunks[0].Sha256Checksum = "intentionally-wrong-checksum";

        _repoMock.Setup(r => r.GetByIdAsync(manifest.Id, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(manifest);
        _providerMock
            .Setup(p => p.ReadChunkAsync($"{manifest.Id}/0", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream(Chunk0));

        var outputPath = Path.Combine(_tempDir, "partial_chunk_fail.bin");

        await Assert.ThrowsAsync<ChecksumMismatchException>(
            () => _sut.DownloadAsync(manifest.Id, outputPath));

        File.Exists(outputPath).Should().BeFalse("partial file must be deleted on chunk checksum failure");
    }

    [Fact]
    public async Task DownloadAsync_FinalChecksumMismatch_DeletesPartialOutputFile()
    {
        var manifest = CreateManifest();
        manifest.Sha256Checksum = "wrong-final-checksum";

        _repoMock.Setup(r => r.GetByIdAsync(manifest.Id, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(manifest);
        _providerMock
            .Setup(p => p.ReadChunkAsync($"{manifest.Id}/0", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream(Chunk0));
        _providerMock
            .Setup(p => p.ReadChunkAsync($"{manifest.Id}/1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream(Chunk1));

        var outputPath = Path.Combine(_tempDir, "partial_final_fail.bin");

        await Assert.ThrowsAsync<ChecksumMismatchException>(
            () => _sut.DownloadAsync(manifest.Id, outputPath));

        File.Exists(outputPath).Should().BeFalse("partial file must be deleted on final checksum failure");
    }

    [Fact]
    public async Task DownloadAsync_ProviderThrows_DeletesPartialOutputFile()
    {
        var manifest = CreateManifest();
        _repoMock.Setup(r => r.GetByIdAsync(manifest.Id, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(manifest);
        _providerMock
            .Setup(p => p.ReadChunkAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Storage failure"));

        var outputPath = Path.Combine(_tempDir, "partial_provider_fail.bin");

        await Assert.ThrowsAsync<IOException>(
            () => _sut.DownloadAsync(manifest.Id, outputPath));

        File.Exists(outputPath).Should().BeFalse("partial file must be deleted when provider throws");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            foreach (var f in Directory.GetFiles(_tempDir))
                File.Delete(f);
            Directory.Delete(_tempDir);
        }
    }
}
