using BitScatter.Application.DTOs;
using BitScatter.Application.Interfaces;
using BitScatter.Application.Services;
using BitScatter.Application.Strategies;
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
        _providerMock.SetupGet(p => p.Name).Returns("node1");
        _providerMock.SetupGet(p => p.ProviderType).Returns(StorageProviderType.FileSystem);
        _providerMock
            .Setup(p => p.SaveChunkAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream s, string key, CancellationToken ct) => key);

        _sut = new UploadService(
            [_providerMock.Object],
            _repoMock.Object,
            _checksumMock.Object,
            new RoundRobinScatteringStrategy(),
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

    [Fact]
    public async Task UploadAsync_NullProviders_UsesAllAvailableProviders()
    {
        var dbProviderMock = new Mock<IStorageProvider>();
        dbProviderMock.SetupGet(p => p.Name).Returns("database");
        dbProviderMock.SetupGet(p => p.ProviderType).Returns(StorageProviderType.Database);
        dbProviderMock
            .Setup(p => p.SaveChunkAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream s, string key, CancellationToken ct) => key);

        var sut = new UploadService(
            [_providerMock.Object, dbProviderMock.Object],
            _repoMock.Object,
            _checksumMock.Object,
            new RoundRobinScatteringStrategy(),
            NullLogger<UploadService>.Instance);

        var options = new UploadOptions
        {
            ChunkSizeBytes = 1024,
            StorageProviders = null // all available
        };

        var result = await sut.UploadAsync(_testFile, options);

        result.Success.Should().BeTrue();
        result.ChunkCount.Should().Be(2);

        // Each provider receives exactly one chunk via round-robin
        _providerMock.Verify(
            p => p.SaveChunkAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
        dbProviderMock.Verify(
            p => p.SaveChunkAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UploadAsync_MultipleProviders_ScattersChunksRoundRobin()
    {
        var dbProviderMock = new Mock<IStorageProvider>();
        dbProviderMock.SetupGet(p => p.Name).Returns("database");
        dbProviderMock.SetupGet(p => p.ProviderType).Returns(StorageProviderType.Database);
        dbProviderMock
            .Setup(p => p.SaveChunkAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream s, string key, CancellationToken ct) => key);

        var sut = new UploadService(
            [_providerMock.Object, dbProviderMock.Object],
            _repoMock.Object,
            _checksumMock.Object,
            new RoundRobinScatteringStrategy(),
            NullLogger<UploadService>.Instance);

        var options = new UploadOptions
        {
            ChunkSizeBytes = 1024,
            StorageProviders = [StorageProviderType.FileSystem, StorageProviderType.Database]
        };

        var result = await sut.UploadAsync(_testFile, options);

        result.Success.Should().BeTrue();
        result.ChunkCount.Should().Be(2);

        _providerMock.Verify(
            p => p.SaveChunkAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
        dbProviderMock.Verify(
            p => p.SaveChunkAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UploadAsync_EmptyProviders_UsesAllAvailableProviders()
    {
        var options = new UploadOptions
        {
            ChunkSizeBytes = 1024,
            StorageProviders = [] // treated same as null
        };

        var result = await _sut.UploadAsync(_testFile, options);

        result.Success.Should().BeTrue();
        result.ChunkCount.Should().Be(2);
    }

    // UploadManyAsync tests

    [Fact]
    public async Task UploadManyAsync_MultipleValidFiles_ReturnsAllSuccessResults()
    {
        var secondFile = Path.Combine(_tempDir, "test2.bin");
        File.WriteAllBytes(secondFile, new byte[1024]);

        var options = new UploadOptions
        {
            ChunkSizeBytes = 1024,
            StorageProviders = [StorageProviderType.FileSystem]
        };

        var result = await _sut.UploadManyAsync([_testFile, secondFile], options);

        result.AllSucceeded.Should().BeTrue();
        result.TotalCount.Should().Be(2);
        result.SuccessCount.Should().Be(2);
        result.FailureCount.Should().Be(0);
        result.Results.Should().AllSatisfy(r => r.Success.Should().BeTrue());
    }

    [Fact]
    public async Task UploadManyAsync_OneFileMissing_ContinuesAndReturnsPartialResults()
    {
        var options = new UploadOptions
        {
            ChunkSizeBytes = 1024,
            StorageProviders = [StorageProviderType.FileSystem]
        };

        var result = await _sut.UploadManyAsync(["/nonexistent/file.bin", _testFile], options);

        result.TotalCount.Should().Be(2);
        result.SuccessCount.Should().Be(1);
        result.FailureCount.Should().Be(1);
        result.AllSucceeded.Should().BeFalse();
        result.Results.Should().ContainSingle(r => !r.Success);
        result.Results.Should().ContainSingle(r => r.Success);
    }

    [Fact]
    public async Task UploadManyAsync_AllFilesMissing_ReturnsAllFailures()
    {
        var options = new UploadOptions { StorageProviders = [StorageProviderType.FileSystem] };

        var result = await _sut.UploadManyAsync(["/missing1.bin", "/missing2.bin"], options);

        result.AllSucceeded.Should().BeFalse();
        result.SuccessCount.Should().Be(0);
        result.FailureCount.Should().Be(2);
    }

    [Fact]
    public async Task UploadManyAsync_EmptyList_ReturnsEmptyBatchResult()
    {
        var options = new UploadOptions { StorageProviders = [StorageProviderType.FileSystem] };

        var result = await _sut.UploadManyAsync([], options);

        result.TotalCount.Should().Be(0);
        result.AllSucceeded.Should().BeTrue();
        _repoMock.Verify(r => r.SaveAsync(It.IsAny<FileManifest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UploadManyAsync_CancellationRequested_StopsBatch()
    {
        var options = new UploadOptions { StorageProviders = [StorageProviderType.FileSystem] };
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => _sut.UploadManyAsync([_testFile, _testFile], options, cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _repoMock.Verify(r => r.SaveAsync(It.IsAny<FileManifest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UploadManyAsync_TwoValidFiles_SavesManifestTwice()
    {
        var secondFile = Path.Combine(_tempDir, "test3.bin");
        File.WriteAllBytes(secondFile, new byte[512]);

        var options = new UploadOptions
        {
            ChunkSizeBytes = 1024,
            StorageProviders = [StorageProviderType.FileSystem]
        };

        await _sut.UploadManyAsync([_testFile, secondFile], options);

        _repoMock.Verify(r => r.SaveAsync(It.IsAny<FileManifest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task UploadManyAsync_FailedFile_ResultContainsFileName()
    {
        var options = new UploadOptions { StorageProviders = [StorageProviderType.FileSystem] };

        var result = await _sut.UploadManyAsync(["/nonexistent/photo.jpg"], options);

        result.Results.Should().ContainSingle();
        var failedResult = result.Results[0];
        failedResult.Success.Should().BeFalse();
        failedResult.FileName.Should().Be("photo.jpg");
        failedResult.ErrorMessage.Should().NotBeNullOrEmpty();
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
