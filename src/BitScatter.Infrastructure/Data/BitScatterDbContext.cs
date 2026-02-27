using BitScatter.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BitScatter.Infrastructure.Data;

public class BitScatterDbContext : DbContext
{
    public BitScatterDbContext(DbContextOptions<BitScatterDbContext> options) : base(options) { }

    public DbSet<FileManifest> FileManifests => Set<FileManifest>();
    public DbSet<ChunkInfo> ChunkInfos => Set<ChunkInfo>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FileManifest>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FileName).IsRequired().HasMaxLength(1024);
            entity.Property(e => e.Sha256Checksum).IsRequired().HasMaxLength(64);
            entity.HasMany(e => e.Chunks)
                  .WithOne(c => c.FileManifest)
                  .HasForeignKey(c => c.FileManifestId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ChunkInfo>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Sha256Checksum).IsRequired().HasMaxLength(64);
            entity.Property(e => e.StorageKey).IsRequired().HasMaxLength(2048);
            entity.HasIndex(e => new { e.FileManifestId, e.ChunkIndex });
        });
    }
}
