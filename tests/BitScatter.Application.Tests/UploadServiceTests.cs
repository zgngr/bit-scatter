using BitScatter.Application.DTOs;
using BitScatter.Application.Interfaces;
using BitScatter.Application.Services;
using BitScatter.Domain.Entities;
using BitScatter.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BitScatter.Application.Tests;

public class UploadServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _testFile;
    private readonly Mock<IFileManifestRepository> _repoMock;
    private readonly Mock<IStorageProvider> _providerMock;
    private readonly Mock<IChecksumService> _checksumMock;
    private readonly UploadService _sut;

    public UploadServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        _testFile = Path.Combine(_tempDir, "test.bin");

        var content = new byte[2048];
        Random.Shared.NextBytes(content);
        File.WriteAllBytes(_testFile, content);

        _repoMock = new Mock<IFileManifestRepository>();
        _checksumMock = new Mock<IChecksumService>();

        _checksumMock
            .Setup(c => c.ComputeSha256Async(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("filechecksum");

        _providerMock = new Mock<IStorageProvider>();
        _providerMock.SetupGet(p => p.ProviderType).Returns(StorageProviderType.FileSystem);
        _providerMock
            .Setup(p => p.SaveChunkAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream s, string key, CancellationToken ct) => key);

        _sut = new UploadService(
            [_providerMock.Object],
            _repoMock.Object,
            _checksumMock.Object,
            NullLogger<UploadService>.Instance);
    }

    [Fact]
    public async Task UploadAsync_ValidFile_ReturnsSuccessResult()
    {
        var options = new UploadOptions
        {
            ChunkSizeBytes = 1024,
            StorageProviders = [StorageProviderType.FileSystem]
        };

        var result = await _sut.UploadAsync(_testFile, options);

        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.FileName.Should().Be("test.bin");
        result.OriginalSize.Should().Be(2048);
        result.FileManifestId.Should().NotBe(Guid.Empty);
        result.ChunkCount.Should().Be(2);
    }

    [Fact]
    public async Task UploadAsync_SavesManifestToRepository()
    {
        var options = new UploadOptions
        {
            ChunkSizeBytes = 1024,
            StorageProviders = [StorageProviderType.FileSystem]
        };

        await _sut.UploadAsync(_testFile, options);

        _repoMock.Verify(r => r.SaveAsync(It.IsAny<FileManifest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UploadAsync_SavesChunksToProvider()
    {
        var options = new UploadOptions
        {
            ChunkSizeBytes = 1024,
            StorageProviders = [StorageProviderType.FileSystem]
        };

        await _sut.UploadAsync(_testFile, options);

        _providerMock.Verify(
            p => p.SaveChunkAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task UploadAsync_FileNotFound_Throws()
    {
        var options = new UploadOptions { StorageProviders = [StorageProviderType.FileSystem] };
        var act = () => _sut.UploadAsync("/nonexistent/file.bin", options);
        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task UploadAsync_NoMatchingProvider_Throws()
    {
        var options = new UploadOptions
        {
            ChunkSizeBytes = 1024,
            StorageProviders = [StorageProviderType.Database]  // No DB provider registered
        };

        var act = () => _sut.UploadAsync(_testFile, options);
        await act.Should().ThrowAsync<InvalidOperationException>();
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
