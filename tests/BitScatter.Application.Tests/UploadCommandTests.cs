using System.Reflection;
using BitScatter.Cli.Commands;
using BitScatter.Domain.Enums;
using FluentAssertions;

namespace BitScatter.Application.Tests;

public class UploadCommandTests
{
    [Fact]
    public void ParseProviders_S3Token_MapsToS3ProviderType()
    {
        var parsed = InvokeParseProviders("s3");

        parsed.Should().Equal(StorageProviderType.S3);
    }

    [Fact]
    public void ParseProviders_MixedTokens_MapsToExpectedProviderTypes()
    {
        var parsed = InvokeParseProviders("filesystem,s3");

        parsed.Should().Equal(StorageProviderType.FileSystem, StorageProviderType.S3);
    }

    private static StorageProviderType[]? InvokeParseProviders(string input)
    {
        var parseMethod = typeof(UploadCommand)
            .GetMethod("ParseProviders", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("UploadCommand.ParseProviders was not found.");

        return (StorageProviderType[]?)parseMethod.Invoke(null, [input]);
    }
}
