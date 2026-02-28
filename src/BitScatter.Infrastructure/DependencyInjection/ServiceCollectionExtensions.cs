using BitScatter.Application.Interfaces;
using BitScatter.Application.Services;
using BitScatter.Application.Strategies;
using BitScatter.Infrastructure.Configuration;
using BitScatter.Infrastructure.Data;
using BitScatter.Infrastructure.Repositories;
using BitScatter.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BitScatter.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBitScatterInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var metadataConnectionString = configuration.GetConnectionString("Metadata")
            ?? "Data Source=bitscatter.db";

        var chunkConnectionString = configuration.GetConnectionString("ChunkStorage");

        services.AddDbContextFactory<BitScatterDbContext>(options =>
            options.UseSqlite(metadataConnectionString));

        if (!string.IsNullOrEmpty(chunkConnectionString))
        {
            services.AddDbContextFactory<ChunkStorageDbContext>(options =>
                options.UseNpgsql(chunkConnectionString));

            services.AddScoped<IStorageProvider>(sp =>
                new DatabaseStorageProvider(
                    sp.GetRequiredService<IDbContextFactory<ChunkStorageDbContext>>(),
                    sp.GetRequiredService<ILogger<DatabaseStorageProvider>>()));
        }

        var fsProviders = configuration
            .GetSection("BitScatter:FileSystemProviders")
            .GetChildren()
            .Select(s => new FileSystemProviderOptions
            {
                Name = s["Name"] ?? string.Empty,
                Path = s["Path"] ?? string.Empty
            })
            .Where(p => !string.IsNullOrEmpty(p.Name) && !string.IsNullOrEmpty(p.Path))
            .ToArray();

        if (fsProviders.Length > 0)
        {
            foreach (var fsProvider in fsProviders)
            {
                var name = fsProvider.Name;
                var path = fsProvider.Path;
                services.AddScoped<IStorageProvider>(sp =>
                    new FileSystemStorageProvider(
                        name,
                        path,
                        sp.GetRequiredService<ILogger<FileSystemStorageProvider>>()));
            }
        }
        else
        {
            // Fallback: single provider from legacy config
            var fileSystemPath = configuration["Storage:FileSystemPath"] ?? "chunks";
            services.AddScoped<IStorageProvider>(sp =>
                new FileSystemStorageProvider(
                    "filesystem",
                    fileSystemPath,
                    sp.GetRequiredService<ILogger<FileSystemStorageProvider>>()));
        }

        services.AddScoped<IFileManifestRepository, FileManifestRepository>();
        services.AddScoped<IChecksumService, ChecksumService>();
        services.AddSingleton<IScatteringStrategy, RoundRobinScatteringStrategy>();
        services.AddSingleton<IChunkingStrategyFactory, FixedSizeChunkingStrategyFactory>();
        services.AddScoped<IUploadService, UploadService>();
        services.AddScoped<IDownloadService, DownloadService>();
        services.AddScoped<IDeleteService, DeleteService>();

        return services;
    }

    public static async Task MigrateAsync(this IServiceProvider serviceProvider)
    {
        var metaFactory = serviceProvider.GetRequiredService<IDbContextFactory<BitScatterDbContext>>();
        await using var metaDb = await metaFactory.CreateDbContextAsync();
        await metaDb.Database.EnsureCreatedAsync();

        var chunkFactory = serviceProvider.GetService<IDbContextFactory<ChunkStorageDbContext>>();
        if (chunkFactory is not null)
        {
            await using var chunkDb = await chunkFactory.CreateDbContextAsync();
            await chunkDb.Database.EnsureCreatedAsync();
        }
    }
}
