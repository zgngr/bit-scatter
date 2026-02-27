using BitScatter.Application.Interfaces;
using BitScatter.Application.Services;
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

        services.AddDbContext<BitScatterDbContext>(options =>
            options.UseSqlite(metadataConnectionString));

        if (!string.IsNullOrEmpty(chunkConnectionString))
        {
            services.AddDbContext<ChunkStorageDbContext>(options =>
                options.UseNpgsql(chunkConnectionString));

            services.AddScoped<IStorageProvider>(sp =>
                new DatabaseStorageProvider(
                    sp.GetRequiredService<ChunkStorageDbContext>(),
                    sp.GetRequiredService<ILogger<DatabaseStorageProvider>>()));
        }

        var fileSystemPath = configuration["Storage:FileSystemPath"] ?? "chunks";

        services.AddScoped<IStorageProvider>(sp =>
            new FileSystemStorageProvider(
                fileSystemPath,
                sp.GetRequiredService<ILogger<FileSystemStorageProvider>>()));

        services.AddScoped<IFileManifestRepository, FileManifestRepository>();
        services.AddScoped<IChecksumService, ChecksumService>();
        services.AddScoped<IUploadService, UploadService>();
        services.AddScoped<IDownloadService, DownloadService>();

        return services;
    }

    public static async Task MigrateAsync(this IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var metaDb = scope.ServiceProvider.GetRequiredService<BitScatterDbContext>();
        await metaDb.Database.EnsureCreatedAsync();

        var chunkDb = scope.ServiceProvider.GetService<ChunkStorageDbContext>();
        if (chunkDb is not null)
            await chunkDb.Database.EnsureCreatedAsync();
    }
}
