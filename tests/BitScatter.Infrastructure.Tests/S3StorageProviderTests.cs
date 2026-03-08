using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using BitScatter.Domain.Enums;
using BitScatter.Domain.Exceptions;
using BitScatter.Infrastructure.Storage;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BitScatter.Infrastructure.Tests;

public class S3StorageProviderTests
{
    [Fact]
    public async Task SaveChunkAsync_UploadsObjectAndReturnsKey()
    {
        var s3Mock = new Mock<IAmazonS3>(MockBehavior.Strict);
        var captured = Array.Empty<byte>();
        s3Mock.Setup(s => s.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PutObjectRequest, CancellationToken>((request, _) =>
            {
                using var ms = new MemoryStream();
                request.InputStream.CopyTo(ms);
                captured = ms.ToArray();
            })
            .ReturnsAsync(new PutObjectResponse());

        var sut = new S3StorageProvider("s3-node", "my-bucket", s3Mock.Object, NullLogger<S3StorageProvider>.Instance);
        using var data = new MemoryStream([1, 2, 3, 4]);

        var key = await sut.SaveChunkAsync(data, "manifest/0");

        key.Should().Be("manifest/0");
        sut.ProviderType.Should().Be(StorageProviderType.S3);
        captured.Should().Equal([1, 2, 3, 4]);
        s3Mock.Verify(s => s.PutObjectAsync(It.Is<PutObjectRequest>(r => r.BucketName == "my-bucket" && r.Key == "manifest/0"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReadChunkAsync_ReturnsExpectedBytes()
    {
        var s3Mock = new Mock<IAmazonS3>(MockBehavior.Strict);
        s3Mock.Setup(s => s.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectResponse
            {
                ResponseStream = new MemoryStream([10, 20, 30])
            });

        var sut = new S3StorageProvider("s3-node", "my-bucket", s3Mock.Object, NullLogger<S3StorageProvider>.Instance);

        await using var stream = await sut.ReadChunkAsync("manifest/2");
        var buf = new byte[3];
        await stream.ReadExactlyAsync(buf);

        buf.Should().Equal([10, 20, 30]);
    }

    [Fact]
    public async Task ReadChunkAsync_MissingKey_ThrowsChunkNotFoundException()
    {
        var s3Mock = new Mock<IAmazonS3>(MockBehavior.Strict);
        s3Mock.Setup(s => s.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("missing") { StatusCode = HttpStatusCode.NotFound });

        var sut = new S3StorageProvider("s3-node", "my-bucket", s3Mock.Object, NullLogger<S3StorageProvider>.Instance);

        var act = () => sut.ReadChunkAsync("missing");
        await act.Should().ThrowAsync<ChunkNotFoundException>();
    }

    [Fact]
    public async Task DeleteChunkAsync_MissingKey_DoesNotThrow()
    {
        var s3Mock = new Mock<IAmazonS3>(MockBehavior.Strict);
        s3Mock.Setup(s => s.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("missing") { StatusCode = HttpStatusCode.NotFound });

        var sut = new S3StorageProvider("s3-node", "my-bucket", s3Mock.Object, NullLogger<S3StorageProvider>.Instance);

        var act = () => sut.DeleteChunkAsync("missing");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExistsAsync_WhenObjectPresent_ReturnsTrue()
    {
        var s3Mock = new Mock<IAmazonS3>(MockBehavior.Strict);
        s3Mock.Setup(s => s.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectMetadataResponse());

        var sut = new S3StorageProvider("s3-node", "my-bucket", s3Mock.Object, NullLogger<S3StorageProvider>.Instance);

        (await sut.ExistsAsync("manifest/0")).Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_WhenMissing_ReturnsFalse()
    {
        var s3Mock = new Mock<IAmazonS3>(MockBehavior.Strict);
        s3Mock.Setup(s => s.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("missing") { StatusCode = HttpStatusCode.NotFound });

        var sut = new S3StorageProvider("s3-node", "my-bucket", s3Mock.Object, NullLogger<S3StorageProvider>.Instance);

        (await sut.ExistsAsync("missing")).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteChunkAsync_TransientFailure_RetriesAndSucceeds()
    {
        var s3Mock = new Mock<IAmazonS3>(MockBehavior.Strict);
        s3Mock.SetupSequence(s => s.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("transient"))
            .ReturnsAsync(new DeleteObjectResponse());

        var sut = new S3StorageProvider("s3-node", "my-bucket", s3Mock.Object, NullLogger<S3StorageProvider>.Instance);

        await sut.DeleteChunkAsync("manifest/9");

        s3Mock.Verify(s => s.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
