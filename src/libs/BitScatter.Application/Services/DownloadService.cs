using System.Security.Cryptography;
using BitScatter.Application.DTOs;
using BitScatter.Application.Interfaces;
using BitScatter.Application.Helpers;
using BitScatter.Domain.Enums;
using BitScatter.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace BitScatter.Application.Services;

public class DownloadService : IDownloadService
{
    private readonly IEnumerable<IStorageProvider> _storageProviders;
    private readonly IFileManifestRepository _manifestRepository;
    private readonly ILogger<DownloadService> _logger;

    public DownloadService(
        IEnumerable<IStorageProvider> storageProviders,
        IFileManifestRepository manifestRepository,
        ILogger<DownloadService> logger)
    {
        _storageProviders = storageProviders;
        _manifestRepository = manifestRepository;
        _logger = logger;
    }

    public Task<DownloadResult> DownloadAsync(
        Guid fileManifestId,
        string outputPath,
        IProgress<(int completed, int total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return DownloadAsync(fileManifestId, outputPath, null, progress, cancellationToken);
    }

    public async Task<DownloadResult> DownloadAsync(
        Guid fileManifestId,
        string outputPath,
        string? decryptionPassword,
        IProgress<(int completed, int total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting download for manifest: {ManifestId}", fileManifestId);

        var manifest = await _manifestRepository.GetByIdAsync(fileManifestId, cancellationToken);
        if (manifest is null)
            throw new KeyNotFoundException($"File manifest with ID '{fileManifestId}' not found.");

        byte[]? fileEncryptionKey = null;
        if (manifest.IsEncrypted)
        {
            if (string.IsNullOrEmpty(decryptionPassword))
                throw new ArgumentException("This file is encrypted. A decryption password is required.");

            if (string.IsNullOrEmpty(manifest.EncryptedKey) ||
                string.IsNullOrEmpty(manifest.EncryptionSalt) ||
                string.IsNullOrEmpty(manifest.EncryptionIv) ||
                string.IsNullOrEmpty(manifest.EncryptionTag))
            {
                throw new InvalidOperationException("Encrypted file manifest is missing critical cryptographic metadata.");
            }

            var encryptedFek = Convert.FromBase64String(manifest.EncryptedKey);
            var salt = Convert.FromBase64String(manifest.EncryptionSalt);
            var iv = Convert.FromBase64String(manifest.EncryptionIv);
            var tag = Convert.FromBase64String(manifest.EncryptionTag);

            var kek = EncryptionHelper.DeriveKey(decryptionPassword, salt);
            try
            {
                fileEncryptionKey = EncryptionHelper.Decrypt(encryptedFek, kek, iv, tag);
            }
            catch (CryptographicException)
            {
                throw new CryptographicException("Incorrect decryption password or tampered manifest metadata.");
            }
        }

        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir))
            Directory.CreateDirectory(outputDir);

        var outputStream = File.Create(outputPath);
        bool downloadSucceeded = false;
        try
        {
            var orderedChunks = manifest.Chunks.OrderBy(c => c.ChunkIndex).ToList();
            int totalChunks = orderedChunks.Count;
            int completedChunks = 0;
            using var fileHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            foreach (var chunkInfo in orderedChunks)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var provider = GetProvider(chunkInfo.ProviderName, chunkInfo.StorageProviderType);

                _logger.LogDebug("Reading chunk {Index} from provider {Name} ({ProviderType}) with key {Key}",
                    chunkInfo.ChunkIndex, chunkInfo.ProviderName, chunkInfo.StorageProviderType, chunkInfo.StorageKey);

                await using var chunkStream = await provider.ReadChunkAsync(chunkInfo.StorageKey, cancellationToken);

                using var memoryStream = new MemoryStream();
                await chunkStream.CopyToAsync(memoryStream, cancellationToken);
                var encryptedBytes = memoryStream.ToArray();

                var actualChecksum = Convert.ToHexString(SHA256.HashData(encryptedBytes)).ToLowerInvariant();
                if (!string.Equals(actualChecksum, chunkInfo.Sha256Checksum, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ChecksumMismatchException(
                        $"Chunk {chunkInfo.ChunkIndex} integrity check failed.",
                        chunkInfo.Sha256Checksum,
                        actualChecksum);
                }

                byte[] plaintextBytes;
                if (manifest.IsEncrypted)
                {
                    if (fileEncryptionKey == null)
                        throw new InvalidOperationException("File encryption key is not initialized.");

                    var (ciphertext, nonce, tag) = EncryptionHelper.UnpackChunkPayload(encryptedBytes);
                    plaintextBytes = EncryptionHelper.Decrypt(ciphertext, fileEncryptionKey, nonce, tag);
                }
                else
                {
                    plaintextBytes = encryptedBytes;
                }

                if (chunkInfo.IsCompressed)
                {
                    plaintextBytes = CompressionHelper.Decompress(plaintextBytes);
                }

                fileHash.AppendData(plaintextBytes);
                await outputStream.WriteAsync(plaintextBytes, cancellationToken);

                progress?.Report((++completedChunks, totalChunks));
            }

            await outputStream.FlushAsync(cancellationToken);

            var finalChecksum = Convert.ToHexString(fileHash.GetCurrentHash()).ToLowerInvariant();
            if (!string.Equals(finalChecksum, manifest.Sha256Checksum, StringComparison.OrdinalIgnoreCase))
            {
                throw new ChecksumMismatchException(
                    "Final file integrity check failed.",
                    manifest.Sha256Checksum,
                    finalChecksum);
            }

            downloadSucceeded = true;
        }
        finally
        {
            await outputStream.DisposeAsync();
            if (!downloadSucceeded && File.Exists(outputPath))
            {
                try { File.Delete(outputPath); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete partial output file: {OutputPath}", outputPath);
                }
            }
        }

        _logger.LogInformation(
            "Download complete. File: {FileName}, saved to: {OutputPath}",
            manifest.FileName, outputPath);

        return new DownloadResult
        {
            FileManifestId = fileManifestId,
            OutputPath = outputPath,
            Success = true
        };
    }

    private IStorageProvider GetProvider(string providerName, StorageProviderType fallbackType)
    {
        // Look up by name first (handles multiple nodes of the same type)
        if (!string.IsNullOrEmpty(providerName))
        {
            var byName = _storageProviders.FirstOrDefault(p => p.Name == providerName);
            if (byName is not null) return byName;
        }

        // Fall back to type-based lookup for backward compatibility
        var byType = _storageProviders.FirstOrDefault(p => p.ProviderType == fallbackType);
        if (byType is null)
            throw new InvalidOperationException($"No storage provider registered for name '{providerName}' or type '{fallbackType}'.");
        return byType;
    }
}
