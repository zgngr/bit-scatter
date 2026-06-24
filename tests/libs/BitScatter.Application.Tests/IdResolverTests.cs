using BitScatter.Application.Interfaces;
using BitScatter.Application.Helpers;
using BitScatter.Domain.Entities;
using FluentAssertions;
using Moq;

namespace BitScatter.Application.Tests;

public class IdResolverTests
{
    private readonly Mock<IFileManifestRepository> _repoMock;

    public IdResolverTests()
    {
        _repoMock = new Mock<IFileManifestRepository>();
    }

    [Fact]
    public async Task ResolveIdAsync_WithValidFullGuid_ResolvesDirectlyWithoutRepositoryQuery()
    {
        // Arrange
        var guid = Guid.NewGuid();
        var input = guid.ToString();

        // Act
        var result = await IdResolver.ResolveIdAsync(_repoMock.Object, input);

        // Assert
        result.Should().Be(guid);
        _repoMock.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResolveIdAsync_WithValidUniquePrefix_ResolvesCorrectly()
    {
        // Arrange
        var targetGuid = Guid.Parse("12345678-abcd-1234-abcd-1234567890ab");
        var otherGuid = Guid.Parse("87654321-dcba-4321-dcba-ba0987654321");

        var manifests = new List<FileManifest>
        {
            new() { Id = targetGuid },
            new() { Id = otherGuid }
        };

        _repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(manifests);

        // Act & Assert
        // Test varying casings and hyphen inclusion
        (await IdResolver.ResolveIdAsync(_repoMock.Object, "12345678")).Should().Be(targetGuid);
        (await IdResolver.ResolveIdAsync(_repoMock.Object, "12345678-abcd")).Should().Be(targetGuid);
        (await IdResolver.ResolveIdAsync(_repoMock.Object, "12345678aBCD1234")).Should().Be(targetGuid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ResolveIdAsync_WithEmptyInput_ThrowsArgumentException(string input)
    {
        // Act
        var act = () => IdResolver.ResolveIdAsync(_repoMock.Object, input);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("File identifier cannot be empty.*");
    }

    [Theory]
    [InlineData("12")]
    [InlineData("123")]
    [InlineData("abc")]
    public async Task ResolveIdAsync_WithTooShortPrefix_ThrowsFormatException(string input)
    {
        // Act
        var act = () => IdResolver.ResolveIdAsync(_repoMock.Object, input);

        // Assert
        await act.Should().ThrowAsync<FormatException>()
            .WithMessage("*Must be a valid GUID or a hexadecimal prefix of at least 4 characters.");
    }

    [Theory]
    [InlineData("1234xyz")]
    [InlineData("abc-def-ghi")]
    public async Task ResolveIdAsync_WithNonHexPrefix_ThrowsFormatException(string input)
    {
        // Act
        var act = () => IdResolver.ResolveIdAsync(_repoMock.Object, input);

        // Assert
        await act.Should().ThrowAsync<FormatException>()
            .WithMessage("*Must be a valid GUID or a hexadecimal prefix of at least 4 characters.");
    }

    [Fact]
    public async Task ResolveIdAsync_WithNoMatches_ThrowsKeyNotFoundException()
    {
        // Arrange
        var targetGuid = Guid.Parse("12345678-abcd-1234-abcd-1234567890ab");
        var manifests = new List<FileManifest> { new() { Id = targetGuid } };

        _repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(manifests);

        // Act
        var act = () => IdResolver.ResolveIdAsync(_repoMock.Object, "abcdef12");

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("No files found matching ID prefix 'abcdef12'.");
    }

    [Fact]
    public async Task ResolveIdAsync_WithAmbiguousMatches_ThrowsInvalidOperationException()
    {
        // Arrange
        var guid1 = Guid.Parse("12345678-abcd-1234-abcd-1234567890ab");
        var guid2 = Guid.Parse("12345678-9999-1111-2222-333333333333");

        var manifests = new List<FileManifest>
        {
            new() { Id = guid1 },
            new() { Id = guid2 }
        };

        _repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(manifests);

        // Act
        var act = () => IdResolver.ResolveIdAsync(_repoMock.Object, "12345678");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Ambiguous ID prefix '12345678'. Multiple matches found: *");
    }
}
