using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using BitScatter.Application.Interfaces;
using BitScatter.Domain.Enums;
using BitScatter.Domain.Exceptions;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace BitScatter.Infrastructure.Storage;

public class S3StorageProvider : IStorageProvider, IDisposable
{
    private readonly IAmazonS3 _s3Client;
    private readonly ILogger<S3StorageProvider> _logger;
    private readonly AsyncRetryPolicy _retryPolicy;
    private readonly string _bucket;

    public string Name { get; }
    public StorageProviderType ProviderType => StorageProviderType.S3;

    public S3StorageProvider(string name, string bucket, IAmazonS3 s3Client, ILogger<S3StorageProvider> logger)
    {
        Name = string.IsNullOrWhiteSpace(name) ? "s3" : name;
        _bucket = bucket;
        _s3Client = s3Client;
        _logger = logger;

        _retryPolicy = Policy
            .Handle<Exception>(ShouldRetry)
            .WaitAndRetryAsync(
                3,
                attempt => TimeSpan.FromMilliseconds(200 * attempt),
                (ex, delay, attempt, _) =>
                    _logger.LogWarning(
                        ex,
                        "Retry {Attempt} after {Delay}ms for S3 operation",
                        attempt,
                        delay.TotalMilliseconds));
    }

    public async Task<string> SaveChunkAsync(Stream data, string key, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Saving chunk to S3 bucket {Bucket} with key: {Key}", _bucket, key);

        using var buffered = await PrepareSeekableStreamAsync(data, cancellationToken);
        var uploadStream = buffered ?? data;
        var initialPosition = uploadStream.CanSeek ? uploadStream.Position : 0;

        await _retryPolicy.ExecuteAsync(async () =>
        {
            if (uploadStream.CanSeek)
                uploadStream.Position = initialPosition;

            await _s3Client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = _bucket,
                Key = key,
                InputStream = uploadStream,
                AutoCloseStream = false
            }, cancellationToken);
        });

        return key;
    }

    public async Task<Stream> ReadChunkAsync(string key, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Reading chunk from S3 bucket {Bucket} with key: {Key}", _bucket, key);

        GetObjectResponse response;
        try
        {
            response = await _retryPolicy.ExecuteAsync(() =>
                _s3Client.GetObjectAsync(new GetObjectRequest
                {
                    BucketName = _bucket,
                    Key = key
                }, cancellationToken));
        }
        catch (AmazonS3Exception ex) when (IsNotFound(ex))
        {
            throw new ChunkNotFoundException(key);
        }

        using (response)
        {
            var content = new MemoryStream();
            await response.ResponseStream.CopyToAsync(content, cancellationToken);
            content.Position = 0;
            return content;
        }
    }

    public async Task DeleteChunkAsync(string key, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Deleting chunk from S3 bucket {Bucket} with key: {Key}", _bucket, key);

        try
        {
            await _retryPolicy.ExecuteAsync(async () =>
            {
                await _s3Client.DeleteObjectAsync(new DeleteObjectRequest
                {
                    BucketName = _bucket,
                    Key = key
                }, cancellationToken);
            });
        }
        catch (AmazonS3Exception ex) when (IsNotFound(ex))
        {
            // Key already absent. Delete should be idempotent.
        }
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            await _retryPolicy.ExecuteAsync(async () =>
            {
                await _s3Client.GetObjectMetadataAsync(new GetObjectMetadataRequest
                {
                    BucketName = _bucket,
                    Key = key
                }, cancellationToken);
            });

            return true;
        }
        catch (AmazonS3Exception ex) when (IsNotFound(ex))
        {
            return false;
        }
    }

    public void Dispose()
    {
        _s3Client.Dispose();
    }

    private static bool IsNotFound(AmazonS3Exception ex)
    {
        return ex.StatusCode == HttpStatusCode.NotFound ||
               string.Equals(ex.ErrorCode, "NoSuchKey", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(ex.ErrorCode, "NotFound", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldRetry(Exception ex)
    {
        if (ex is OperationCanceledException)
            return false;

        if (ex is AmazonS3Exception s3Ex && IsNotFound(s3Ex))
            return false;

        return true;
    }

    private static async Task<MemoryStream?> PrepareSeekableStreamAsync(Stream source, CancellationToken cancellationToken)
    {
        if (source.CanSeek)
            return null;

        var buffered = new MemoryStream();
        await source.CopyToAsync(buffered, cancellationToken);
        buffered.Position = 0;
        return buffered;
    }
}
