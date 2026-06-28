using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using BitScatter.Application.DTOs;
using BitScatter.Application.Interfaces;
using BitScatter.Application.Services;
using BitScatter.Application.Strategies;
using BitScatter.Domain.Entities;
using BitScatter.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace BitScatter.Application.Tests;

public class EncryptionIntegrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _testFile;
    private readonly byte[] _testFileContent;
    private readonly ConcurrentDictionary<Guid, FileManifest> _manifests = new();
    private readonly ConcurrentDictionary<string, byte[]> _storage = new();
    
    private readonly Mock<IFileManifestRepository> _repoMock;
    private readonly Mock<IStorageProvider> _providerMock;
    private readonly IChunkingStrategyFactory _chunkingFactory;
    
    private readonly UploadService _uploadService;
    private readonly DownloadService _downloadService;

    public EncryptionIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        _testFile = Path.Combine(_tempDir, "source.bin");

        _testFileContent = new byte[5 * 1024]; // 5 KB
        RandomNumberGenerator.Fill(_testFileContent);
        File.WriteAllBytes(_testFile, _testFileContent);

        // Setup FileManifestRepository mock using in-memory state
        _repoMock = new Mock<IFileManifestRepository>();
        _repoMock.Setup(r => r.SaveAsync(It.IsAny<FileManifest>(), It.IsAny<CancellationToken>()))
            .Callback<FileManifest, CancellationToken>((m, ct) => _manifests[m.Id] = m)
            .Returns(Task.CompletedTask);
            
        _repoMock.Setup(r => r.CompleteAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<ChunkInfo>>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, IReadOnlyList<ChunkInfo>, CancellationToken>((id, checksum, chunks, ct) =>
            {
                var manifest = _manifests[id];
                manifest.Sha256Checksum = checksum;
                manifest.Status = ManifestStatus.Complete;
                manifest.Chunks.AddRange(chunks);
            })
            .Returns(Task.CompletedTask);

        _repoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken ct) => _manifests.TryGetValue(id, out var m) ? m : null);

        // Setup StorageProvider mock using in-memory dictionary
        _providerMock = new Mock<IStorageProvider>();
        _providerMock.SetupGet(p => p.Name).Returns("node1");
        _providerMock.SetupGet(p => p.ProviderType).Returns(StorageProviderType.FileSystem);
        
        _providerMock.Setup(p => p.SaveChunkAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream s, string key, CancellationToken ct) =>
            {
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                _storage[key] = ms.ToArray();
                return key;
            });

        _providerMock.Setup(p => p.ReadChunkAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken ct) => new MemoryStream(_storage[key]));

        _chunkingFactory = new FixedSizeChunkingStrategyFactory(NullLogger<FixedSizeChunkingStrategy>.Instance);

        _uploadService = new UploadService(
            [_providerMock.Object],
            _repoMock.Object,
            new RoundRobinPlacementStrategy(),
            _chunkingFactory,
            NullLogger<UploadService>.Instance);

        _downloadService = new DownloadService(
            [_providerMock.Object],
            _repoMock.Object,
            NullLogger<DownloadService>.Instance);
    }

    [Fact]
    public async Task EncryptedUploadAndDownload_RoundtripsSuccessfully()
    {
        var password = "test-encryption-password";
        var options = new UploadOptions
        {
            ChunkSizeBytes = 1024,
            StorageProviders = [StorageProviderType.FileSystem],
            EncryptionPassword = password,
            ObfuscateStorageKeys = true
        };

        // 1. Upload file
        var uploadBatchResult = await _uploadService.UploadManyAsync([_testFile], options);
        var uploadResult = uploadBatchResult.Results.Should().ContainSingle().Subject;
        uploadResult.Success.Should().BeTrue();

        var manifestId = uploadResult.FileManifestId;
        _manifests.Should().ContainKey(manifestId);
        var manifest = _manifests[manifestId];

        manifest.IsEncrypted.Should().BeTrue();
        manifest.EncryptedKey.Should().NotBeNullOrEmpty();
        manifest.EncryptionSalt.Should().NotBeNullOrEmpty();
        manifest.EncryptionIv.Should().NotBeNullOrEmpty();
        manifest.EncryptionTag.Should().NotBeNullOrEmpty();

        // Check storage keys are obfuscated
        foreach (var chunk in manifest.Chunks)
        {
            chunk.StorageKey.Should().StartWith("chunks/");
            chunk.StorageKey.Should().NotContain(manifestId.ToString());
            _storage.Should().ContainKey(chunk.StorageKey);
        }

        // 2. Download file
        var downloadPath = Path.Combine(_tempDir, "downloaded.bin");
        var downloadResult = await _downloadService.DownloadAsync(manifestId, downloadPath, decryptionPassword: password);

        downloadResult.Success.Should().BeTrue();
        
        var downloadedContent = await File.ReadAllBytesAsync(downloadPath);
        downloadedContent.Should().Equal(_testFileContent);
    }

    [Fact]
    public async Task UploadAndDownload_WithCompressionAndEncryption_RoundtripsSuccessfully()
    {
        // Arrange
        var compressibleFilePath = Path.Combine(_tempDir, "compressible_integration.bin");
        var compressibleContent = new byte[5 * 1024]; // 5 KB
        Array.Fill(compressibleContent, (byte)'X');
        await File.WriteAllBytesAsync(compressibleFilePath, compressibleContent);

        var password = "test-encryption-compression-password";
        var options = new UploadOptions
        {
            ChunkSizeBytes = 1024,
            StorageProviders = [StorageProviderType.FileSystem],
            EncryptionPassword = password,
            EnableCompression = true,
            ObfuscateStorageKeys = true
        };

        // Act
        // 1. Upload
        var uploadBatchResult = await _uploadService.UploadManyAsync([compressibleFilePath], options);
        var uploadResult = uploadBatchResult.Results.Should().ContainSingle().Subject;
        uploadResult.Success.Should().BeTrue();

        var manifestId = uploadResult.FileManifestId;
        _manifests.Should().ContainKey(manifestId);
        var manifest = _manifests[manifestId];

        // Assert chunks are compressed
        manifest.Chunks.Should().NotBeEmpty();
        foreach (var chunk in manifest.Chunks)
        {
            chunk.IsCompressed.Should().BeTrue();
        }

        // 2. Download
        var downloadPath = Path.Combine(_tempDir, "downloaded_compressed.bin");
        var downloadResult = await _downloadService.DownloadAsync(manifestId, downloadPath, decryptionPassword: password);

        // Assert
        downloadResult.Success.Should().BeTrue();
        
        var downloadedContent = await File.ReadAllBytesAsync(downloadPath);
        downloadedContent.Should().Equal(compressibleContent);
    }

    [Fact]
    public async Task Download_IncorrectPassword_ThrowsCryptographicException()
    {
        var password = "correct-password";
        var options = new UploadOptions
        {
            ChunkSizeBytes = 1024,
            StorageProviders = [StorageProviderType.FileSystem],
            EncryptionPassword = password
        };

        var uploadBatchResult = await _uploadService.UploadManyAsync([_testFile], options);
        var uploadResult = uploadBatchResult.Results.Should().ContainSingle().Subject;
        var manifestId = uploadResult.FileManifestId;

        var downloadPath = Path.Combine(_tempDir, "wrong_password.bin");

        Func<Task> act = () => _downloadService.DownloadAsync(manifestId, downloadPath, decryptionPassword: "wrong-password");

        await act.Should().ThrowAsync<CryptographicException>();
    }

    [Fact]
    public async Task Download_MissingPassword_ThrowsArgumentException()
    {
        var password = "correct-password";
        var options = new UploadOptions
        {
            ChunkSizeBytes = 1024,
            StorageProviders = [StorageProviderType.FileSystem],
            EncryptionPassword = password
        };

        var uploadBatchResult = await _uploadService.UploadManyAsync([_testFile], options);
        var uploadResult = uploadBatchResult.Results.Should().ContainSingle().Subject;
        var manifestId = uploadResult.FileManifestId;

        var downloadPath = Path.Combine(_tempDir, "missing_password.bin");

        Func<Task> act = () => _downloadService.DownloadAsync(manifestId, downloadPath, decryptionPassword: null);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }
}
