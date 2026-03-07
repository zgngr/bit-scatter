using BitScatter.Application.DTOs;
using FluentAssertions;

namespace BitScatter.Application.Tests;

public class UploadOptionsTests
{
    [Fact]
    public void ChunkSizeBytes_DefaultValue_IsOneMb()
    {
        var options = new UploadOptions();
        options.ChunkSizeBytes.Should().Be(1024 * 1024);
    }

    [Fact]
    public void ChunkSizeBytes_SetToZero_Throws()
    {
        var options = new UploadOptions();
        var act = () => options.ChunkSizeBytes = 0;
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ChunkSizeBytes_SetToNegative_Throws()
    {
        var options = new UploadOptions();
        var act = () => options.ChunkSizeBytes = -1;
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ChunkSizeBytes_ExceedsMax_Throws()
    {
        var options = new UploadOptions();
        var act = () => options.ChunkSizeBytes = UploadOptions.MaxChunkSizeBytes + 1;
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ChunkSizeBytes_SetToMax_DoesNotThrow()
    {
        var options = new UploadOptions();
        var act = () => options.ChunkSizeBytes = UploadOptions.MaxChunkSizeBytes;
        act.Should().NotThrow();
    }

    [Fact]
    public void ChunkSizeBytes_SetToValidValue_Succeeds()
    {
        var options = new UploadOptions();
        options.ChunkSizeBytes = 512 * 1024;
        options.ChunkSizeBytes.Should().Be(512 * 1024);
    }

    [Fact]
    public void MaxInFlightChunks_Default_IsEight()
    {
        var options = new UploadOptions();
        options.MaxInFlightChunks.Should().Be(8);
    }

    [Fact]
    public void MaxInFlightChunks_SetToZero_Throws()
    {
        var options = new UploadOptions();
        var act = () => options.MaxInFlightChunks = 0;
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void MaxInFlightChunks_SetToNegative_Throws()
    {
        var options = new UploadOptions();
        var act = () => options.MaxInFlightChunks = -1;
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void MaxInFlightChunks_SetToValidValue_Succeeds()
    {
        var options = new UploadOptions();
        options.MaxInFlightChunks = 16;
        options.MaxInFlightChunks.Should().Be(16);
    }
}
