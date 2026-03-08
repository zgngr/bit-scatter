using BitScatter.Application.DTOs;
using BitScatter.Application.Interfaces;
using BitScatter.Application.Services;
using BitScatter.Application.Strategies;
using BitScatter.Domain.Entities;
using BitScatter.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Security.Cryptography;

namespace BitScatter.Application.Tests;

public class UploadServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _testFile;
    private readonly byte[] _testFileContent;
    private readonly Mock<IFileManifestRepository> _repoMock;
    private readonly Mock<IStorageProvider> _providerMock;
    private readonly IChunkingStrategyFactory _chunkingFactory;
    private readonly UploadService _sut;

    public UploadServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        _testFile = Path.Combine(_tempDir, "test.bin");

        _testFileContent = new byte[2048];
        Random.Shared.NextBytes(_testFileContent);
        File.WriteAllBytes(_testFile, _testFileContent);

        _repoMock = new Mock<IFileManifestRepository>();

        _providerMock = new Mock<IStorageProvider>();
        _providerMock.SetupGet(p => p.Name).Returns("node1");
        _providerMock.SetupGet(p => p.ProviderType).Returns(StorageProviderType.FileSystem);
        _providerMock
            .Setup(p => p.SaveChunkAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream s, string key, CancellationToken ct) => key);

        _repoMock
            .Setup(r => r.CompleteAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<ChunkInfo>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _chunkingFactory = new FixedSizeChunkingStrategyFactory(
            NullLogger<FixedSizeChunkingStrategy>.Instance);

        _sut = new UploadService(
            [_providerMock.Object],
            _repoMock.Object,
            new RoundRobinPlacementStrategy(),
            _chunkingFactory,
            NullLogger<UploadService>.Instance);
    }

    [Fact]
    public async Task UploadManyAsync_SingleValidFile_ReturnsSuccessResult()
    {
        var options = new UploadOptions
        {
            ChunkSizeBytes = 1024,
            StorageProviders = [StorageProviderType.FileSystem]
        };

        var batchResult = await _sut.UploadManyAsync([_testFile], options);
        var result = batchResult.Results.Should().ContainSingle().Subject;

        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.FileName.Should().Be("test.bin");
        result.OriginalSize.Should().Be(2048);
        result.FileManifestId.Should().NotBe(Guid.Empty);
        result.ChunkCount.Should().Be(2);
        batchResult.TotalCount.Should().Be(1);
        batchResult.SuccessCount.Should().Be(1);
        batchResult.FailureCount.Should().Be(0);
    }

    [Fact]
    public async Task UploadManyAsync_SingleValidFile_SavesManifestToRepository()
    {
        var options = new UploadOptions
        {
            ChunkSizeBytes = 1024,
            StorageProviders = [StorageProviderType.FileSystem]
        };

        await _sut.UploadManyAsync([_testFile], options);

        // Phase 1: pending manifest saved
        _repoMock.Verify(r => r.SaveAsync(It.IsAny<FileManifest>(), It.IsAny<CancellationToken>()), Times.Once);
        // Phase 2: manifest completed with chunks
        _repoMock.Verify(r => r.CompleteAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<ChunkInfo>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UploadManyAsync_SingleValidFile_SavesChunksToProvider()
    {
        var options = new UploadOptions
        {
            ChunkSizeBytes = 1024,
            StorageProviders = [StorageProviderType.FileSystem]
        };

        await _sut.UploadManyAsync([_testFile], options);

        _providerMock.Verify(
            p => p.SaveChunkAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task UploadManyAsync_SingleMissingFile_ReturnsFailedResult()
    {
        var options = new UploadOptions { StorageProviders = [StorageProviderType.FileSystem] };
        var result = await _sut.UploadManyAsync(["/nonexistent/file.bin"], options);

        result.TotalCount.Should().Be(1);
        result.SuccessCount.Should().Be(0);
        result.FailureCount.Should().Be(1);
        result.AllSucceeded.Should().BeFalse();
        result.Results.Should().ContainSingle(r => !r.Success);
    }

    [Fact]
    public async Task UploadManyAsync_SingleFileWithNoMatchingProvider_ReturnsFailedResult()
    {
        var options = new UploadOptions
        {
            ChunkSizeBytes = 1024,
            StorageProviders = [StorageProviderType.Database]  // No DB provider registered
        };

        var result = await _sut.UploadManyAsync([_testFile], options);

        result.TotalCount.Should().Be(1);
        result.SuccessCount.Should().Be(0);
        result.FailureCount.Should().Be(1);
        result.AllSucceeded.Should().BeFalse();
        result.Results.Should().ContainSingle(r => !r.Success);
    }

    [Fact]
    public async Task UploadManyAsync_SingleFileWithNullProviders_UsesAllAvailableProviders()
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
            new RoundRobinPlacementStrategy(),
            _chunkingFactory,
            NullLogger<UploadService>.Instance);

        var options = new UploadOptions
        {
            ChunkSizeBytes = 1024,
            StorageProviders = null // all available
        };

        var batchResult = await sut.UploadManyAsync([_testFile], options);
        var result = batchResult.Results.Should().ContainSingle().Subject;

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
    public async Task UploadManyAsync_SingleFileWithMultipleProviders_ScattersChunksRoundRobin()
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
            new RoundRobinPlacementStrategy(),
            _chunkingFactory,
            NullLogger<UploadService>.Instance);

        var options = new UploadOptions
        {
            ChunkSizeBytes = 1024,
            StorageProviders = [StorageProviderType.FileSystem, StorageProviderType.Database]
        };

        var batchResult = await sut.UploadManyAsync([_testFile], options);
        var result = batchResult.Results.Should().ContainSingle().Subject;

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
    public async Task UploadManyAsync_SingleFileWithFileSystemAndS3Providers_ScattersChunksRoundRobin()
    {
        var s3ProviderMock = new Mock<IStorageProvider>();
        s3ProviderMock.SetupGet(p => p.Name).Returns("s3");
        s3ProviderMock.SetupGet(p => p.ProviderType).Returns(StorageProviderType.S3);
        s3ProviderMock
            .Setup(p => p.SaveChunkAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream s, string key, CancellationToken ct) => key);

        var sut = new UploadService(
            [_providerMock.Object, s3ProviderMock.Object],
            _repoMock.Object,
            new RoundRobinPlacementStrategy(),
            _chunkingFactory,
            NullLogger<UploadService>.Instance);

        var options = new UploadOptions
        {
            ChunkSizeBytes = 1024,
            StorageProviders = [StorageProviderType.FileSystem, StorageProviderType.S3]
        };

        var batchResult = await sut.UploadManyAsync([_testFile], options);
        var result = batchResult.Results.Should().ContainSingle().Subject;

        result.Success.Should().BeTrue();
        result.ChunkCount.Should().Be(2);

        _providerMock.Verify(
            p => p.SaveChunkAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
        s3ProviderMock.Verify(
            p => p.SaveChunkAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UploadManyAsync_SingleFileWithEmptyProviders_UsesAllAvailableProviders()
    {
        var options = new UploadOptions
        {
            ChunkSizeBytes = 1024,
            StorageProviders = [] // treated same as null
        };

        var batchResult = await _sut.UploadManyAsync([_testFile], options);
        var result = batchResult.Results.Should().ContainSingle().Subject;

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

    [Fact]
    public async Task UploadManyAsync_SingleFileProviderFailsMidUpload_ReturnsFailureAndRollsBackSavedChunks()
    {
        // Arrange: 2-chunk file (2048 bytes / 1024 chunk size); second save throws
        var providerMock = new Mock<IStorageProvider>();
        providerMock.SetupGet(p => p.Name).Returns("node1");
        providerMock.SetupGet(p => p.ProviderType).Returns(StorageProviderType.FileSystem);
        providerMock
            .SetupSequence(p => p.SaveChunkAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("chunk0")
            .ThrowsAsync(new IOException("Storage failure"));

        var sut = new UploadService(
            [providerMock.Object],
            _repoMock.Object,
            new RoundRobinPlacementStrategy(),
            _chunkingFactory,
            NullLogger<UploadService>.Instance);

        var options = new UploadOptions { ChunkSizeBytes = 1024, StorageProviders = [StorageProviderType.FileSystem] };

        // Act
        var result = await sut.UploadManyAsync([_testFile], options);
        result.TotalCount.Should().Be(1);
        result.SuccessCount.Should().Be(0);
        result.FailureCount.Should().Be(1);

        // Assert: pending manifest was saved, first storage chunk rolled back, pending manifest cleaned up
        _repoMock.Verify(r => r.SaveAsync(It.IsAny<FileManifest>(), It.IsAny<CancellationToken>()), Times.Once);
        providerMock.Verify(
            p => p.DeleteChunkAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _repoMock.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
        _repoMock.Verify(r => r.CompleteAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<ChunkInfo>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UploadManyAsync_SingleFileSavePendingManifestFails_ReturnsFailureWithoutChunkWrites()
    {
        // SaveAsync (pending phase) throws before any chunks are uploaded
        _repoMock
            .Setup(r => r.SaveAsync(It.IsAny<FileManifest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB unavailable"));

        var options = new UploadOptions { ChunkSizeBytes = 1024, StorageProviders = [StorageProviderType.FileSystem] };

        var result = await _sut.UploadManyAsync([_testFile], options);
        result.TotalCount.Should().Be(1);
        result.SuccessCount.Should().Be(0);
        result.FailureCount.Should().Be(1);

        // No chunks were ever written to storage, so nothing to roll back
        _providerMock.Verify(
            p => p.SaveChunkAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _providerMock.Verify(
            p => p.DeleteChunkAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UploadManyAsync_SingleFileCompleteManifestFails_ReturnsFailureAndRollsBackAllChunks()
    {
        // All chunks save to storage OK, but CompleteAsync (phase 2) throws
        _repoMock
            .Setup(r => r.CompleteAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<ChunkInfo>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB unavailable on complete"));

        var options = new UploadOptions { ChunkSizeBytes = 1024, StorageProviders = [StorageProviderType.FileSystem] };

        var result = await _sut.UploadManyAsync([_testFile], options);
        result.TotalCount.Should().Be(1);
        result.SuccessCount.Should().Be(0);
        result.FailureCount.Should().Be(1);

        // Both storage chunks rolled back
        _providerMock.Verify(
            p => p.DeleteChunkAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        // Pending manifest cleaned up from DB
        _repoMock.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UploadManyAsync_MaxConcurrentUploads_IsRespectedAsParallelismCap()
    {
        // Arrange: three files, cap = 1 (sequential)
        var file2 = Path.Combine(_tempDir, "test_s2b.bin");
        var file3 = Path.Combine(_tempDir, "test_s2c.bin");
        File.WriteAllBytes(file2, new byte[512]);
        File.WriteAllBytes(file3, new byte[512]);

        var options = new UploadOptions
        {
            ChunkSizeBytes = 1024,
            StorageProviders = [StorageProviderType.FileSystem],
            MaxConcurrentUploads = 1   // explicitly sequential
        };

        var result = await _sut.UploadManyAsync([_testFile, file2, file3], options);

        result.AllSucceeded.Should().BeTrue();
        result.TotalCount.Should().Be(3);
    }

    [Fact]
    public async Task UploadManyAsync_SingleFileCompleteAsyncReceivesChecksumAndOrderedChunks()
    {
        var options = new UploadOptions
        {
            ChunkSizeBytes = 256,
            StorageProviders = [StorageProviderType.FileSystem],
            MaxInFlightChunks = 4
        };

        string? actualChecksum = null;
        IReadOnlyList<ChunkInfo>? actualChunks = null;

        _repoMock
            .Setup(r => r.CompleteAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<ChunkInfo>>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, IReadOnlyList<ChunkInfo>, CancellationToken>((_, checksum, chunks, _) =>
            {
                actualChecksum = checksum;
                actualChunks = chunks;
            })
            .Returns(Task.CompletedTask);

        await _sut.UploadManyAsync([_testFile], options);

        var expectedChecksum = Convert.ToHexString(SHA256.HashData(_testFileContent)).ToLowerInvariant();
        actualChecksum.Should().Be(expectedChecksum);
        actualChunks.Should().NotBeNull();
        actualChunks!.Select(c => c.ChunkIndex).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task UploadManyAsync_SingleFileMaxInFlightChunks_RespectsConcurrencyCap()
    {
        var active = 0;
        var maxObserved = 0;

        _providerMock
            .Setup(p => p.SaveChunkAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async (Stream _, string key, CancellationToken _) =>
            {
                var current = Interlocked.Increment(ref active);
                while (true)
                {
                    var snapshot = Volatile.Read(ref maxObserved);
                    if (current <= snapshot)
                        break;
                    if (Interlocked.CompareExchange(ref maxObserved, current, snapshot) == snapshot)
                        break;
                }

                await Task.Delay(30);
                Interlocked.Decrement(ref active);
                return key;
            });

        var options = new UploadOptions
        {
            ChunkSizeBytes = 128,
            StorageProviders = [StorageProviderType.FileSystem],
            MaxInFlightChunks = 2
        };

        await _sut.UploadManyAsync([_testFile], options);

        maxObserved.Should().BeLessThanOrEqualTo(2);
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
