using Amazon.S3;
using BitScatter.Application.Interfaces;
using BitScatter.Domain.Enums;
using BitScatter.Infrastructure.DependencyInjection;
using BitScatter.Infrastructure.Storage;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BitScatter.Infrastructure.Tests;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddBitScatterInfrastructure_WithValidS3Config_RegistersS3Provider()
    {
        var config = BuildConfiguration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Metadata"] = "Data Source=:memory:",
            ["BitScatter:S3:Name"] = "s3-primary",
            ["BitScatter:S3:Bucket"] = "chunks-bucket",
            ["BitScatter:S3:Region"] = "us-east-1",
            ["BitScatter:S3:AccessKey"] = "access",
            ["BitScatter:S3:SecretKey"] = "secret"
        });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBitScatterInfrastructure(config);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var storageProviders = scope.ServiceProvider.GetServices<IStorageProvider>().ToList();

        storageProviders.Should().ContainSingle(p => p.ProviderType == StorageProviderType.S3 && p.Name == "s3-primary");
    }

    [Fact]
    public void AddBitScatterInfrastructure_S3ConfiguredWithMissingRequiredFields_Throws()
    {
        var config = BuildConfiguration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Metadata"] = "Data Source=:memory:",
            ["BitScatter:S3:Name"] = "s3-primary",
            ["BitScatter:S3:Region"] = "us-east-1",
            ["BitScatter:S3:AccessKey"] = "access",
            ["BitScatter:S3:SecretKey"] = "secret"
        });

        var services = new ServiceCollection();
        services.AddLogging();

        var act = () => services.AddBitScatterInfrastructure(config);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*BitScatter:S3 is configured but missing required field(s): Bucket*");
    }

    [Fact]
    public void AddBitScatterInfrastructure_WithEndpointAndPathStyle_AppliesClientConfiguration()
    {
        var config = BuildConfiguration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Metadata"] = "Data Source=:memory:",
            ["BitScatter:S3:Bucket"] = "chunks-bucket",
            ["BitScatter:S3:Region"] = "us-east-1",
            ["BitScatter:S3:AccessKey"] = "access",
            ["BitScatter:S3:SecretKey"] = "secret",
            ["BitScatter:S3:Endpoint"] = "http://localhost:9000",
            ["BitScatter:S3:ForcePathStyle"] = "true"
        });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBitScatterInfrastructure(config);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var s3Provider = scope.ServiceProvider.GetServices<IStorageProvider>()
            .OfType<S3StorageProvider>()
            .Single();

        var client = GetClient(s3Provider);
        client.Config.ServiceURL.Should().StartWith("http://localhost:9000");
        ((AmazonS3Config)client.Config).ForcePathStyle.Should().BeTrue();
    }

    private static IConfiguration BuildConfiguration(IDictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private static IAmazonS3 GetClient(S3StorageProvider provider)
    {
        var field = typeof(S3StorageProvider).GetField("_s3Client", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?? throw new InvalidOperationException("Unable to inspect S3 client.");

        return (IAmazonS3)(field.GetValue(provider)
            ?? throw new InvalidOperationException("S3 client was null."));
    }
}
