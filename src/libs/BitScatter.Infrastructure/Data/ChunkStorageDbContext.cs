using Microsoft.EntityFrameworkCore;

namespace BitScatter.Infrastructure.Data;

public class ChunkStorageDbContext : DbContext
{
    public ChunkStorageDbContext(DbContextOptions<ChunkStorageDbContext> options) : base(options) { }

    public DbSet<ChunkStorage> ChunkStorages => Set<ChunkStorage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ChunkStorage>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.StorageKey).IsUnique();
            entity.Property(e => e.StorageKey).IsRequired().HasMaxLength(2048);
        });
    }
}
