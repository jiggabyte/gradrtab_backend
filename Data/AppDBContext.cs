using Microsoft.EntityFrameworkCore;
using GradrTab.Models;

namespace GradrTab.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users { get; set; }

    public DbSet<Document> Documents { get; set; }

    public DbSet<DocumentEntry> DocumentEntries { get; set; }

    // Optional: Configure table details using Fluent API
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Ensure emails are unique in the PostgreSQL database
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();

        // Store the enum values as readable strings instead of integers
        modelBuilder.Entity<Document>()
            .Property(d => d.DocumentType)
            .HasConversion<string>()
            .HasMaxLength(20);

        modelBuilder.Entity<Document>()
            .Property(d => d.Status)
            .HasConversion<string>()
            .HasMaxLength(20);

        // Deleting a document removes all of the information extracted from it
        modelBuilder.Entity<Document>()
            .HasMany(d => d.Entries)
            .WithOne(e => e.Document!)
            .HasForeignKey(e => e.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DocumentEntry>()
            .HasIndex(e => e.DocumentId);

        // Uploads are normally listed newest first
        modelBuilder.Entity<Document>()
            .HasIndex(d => d.CreatedAt);
    }

}

