using BitScatter.Domain.Exceptions;
using BitScatter.Infrastructure.Storage;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace BitScatter.Infrastructure.Tests;

public class FileSystemStorageProviderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileSystemStorageProvider _sut;

    public FileSystemStorageProviderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "bitscatter-tests-" + Guid.NewGuid());
        _sut = new FileSystemStorageProvider("test-node", _tempDir, NullLogger<FileSystemStorageProvider>.Instance);
    }

    [Fact]
    public async Task SaveChunkAsync_WritesToDisk()
    {
        var data = new byte[] { 1, 2, 3, 4, 5 };
        using var stream = new MemoryStream(data);

        await _sut.SaveChunkAsync(stream, "test-key");

        var exists = await _sut.ExistsAsync("test-key");
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task ReadChunkAsync_ReturnsCorrectData()
    {
        var data = new byte[] { 10, 20, 30 };
        using var writeStream = new MemoryStream(data);
        await _sut.SaveChunkAsync(writeStream, "read-key");

        await using var readStream = await _sut.ReadChunkAsync("read-key");
        var result = new byte[readStream.Length];
        await readStream.ReadExactlyAsync(result);

        result.Should().BeEquivalentTo(data);
    }

    [Fact]
    public async Task ReadChunkAsync_MissingKey_ThrowsChunkNotFoundException()
    {
        var act = () => _sut.ReadChunkAsync("nonexistent/key");
        await act.Should().ThrowAsync<ChunkNotFoundException>();
    }

    [Fact]
    public async Task DeleteChunkAsync_RemovesFile()
    {
        var data = new byte[] { 1, 2, 3 };
        using var stream = new MemoryStream(data);
        await _sut.SaveChunkAsync(stream, "del-key");

        await _sut.DeleteChunkAsync("del-key");

        var exists = await _sut.ExistsAsync("del-key");
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task ExistsAsync_NonExistent_ReturnsFalse()
    {
        var exists = await _sut.ExistsAsync("does/not/exist");
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task SaveChunkAsync_NestedKey_CreatesDirectories()
    {
        var data = new byte[] { 1, 2, 3 };
        var key = "manifests/abc-123/42";
        using var stream = new MemoryStream(data);

        await _sut.SaveChunkAsync(stream, key);

        var exists = await _sut.ExistsAsync(key);
        exists.Should().BeTrue();
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("../sibling/secret")]
    [InlineData("a/../../escape")]
    public async Task SaveChunkAsync_TraversalKey_Throws(string key)
    {
        using var stream = new MemoryStream([1, 2, 3]);
        var act = () => _sut.SaveChunkAsync(stream, key);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("../sibling/secret")]
    [InlineData("a/../../escape")]
    public async Task ReadChunkAsync_TraversalKey_Throws(string key)
    {
        var act = () => _sut.ReadChunkAsync(key);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("../sibling/secret")]
    [InlineData("a/../../escape")]
    public async Task DeleteChunkAsync_TraversalKey_Throws(string key)
    {
        var act = () => _sut.DeleteChunkAsync(key);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("../sibling/secret")]
    [InlineData("a/../../escape")]
    public async Task ExistsAsync_TraversalKey_Throws(string key)
    {
        var act = () => _sut.ExistsAsync(key);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            foreach (var file in Directory.GetFiles(_tempDir, "*", SearchOption.AllDirectories))
                File.Delete(file);
            foreach (var dir in Directory.GetDirectories(_tempDir, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
                if (Directory.Exists(dir)) Directory.Delete(dir);
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir);
        }
    }
}
