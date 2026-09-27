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

    public DbSet<PasswordResetToken> PasswordResetTokens { get; set; }

    public DbSet<Rubric> Rubrics { get; set; }

    public DbSet<RubricCriterion> RubricCriteria { get; set; }

    public DbSet<RubricLevel> RubricLevels { get; set; }

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

        // A reset link is looked up by its hash, so that lookup has to be unique
        modelBuilder.Entity<PasswordResetToken>()
            .HasIndex(t => t.TokenHash)
            .IsUnique();

        // Supports the per user rate limit on how many links may be asked for
        modelBuilder.Entity<PasswordResetToken>()
            .HasIndex(t => new { t.UserId, t.CreatedAt });

        // Removing a user removes their outstanding reset links as well
        modelBuilder.Entity<PasswordResetToken>()
            .HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // The client supplied rubric key is unique per user, so two users may
        // both own a rubric called rub_essay
        modelBuilder.Entity<Rubric>()
            .HasIndex(r => new { r.UploadedByUserId, r.RubricKey })
            .IsUnique();

        // Removing a rubric takes its criteria and their levels with it
        modelBuilder.Entity<RubricCriterion>()
            .HasOne(c => c.Rubric)
            .WithMany(r => r.Criteria)
            .HasForeignKey(c => c.RubricId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<RubricLevel>()
            .HasOne(l => l.Criterion)
            .WithMany(c => c.Levels)
            .HasForeignKey(l => l.CriterionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Rubric>()
            .HasIndex(r => r.CreatedAt);

        modelBuilder.Entity<RubricCriterion>()
            .HasIndex(c => new { c.RubricId, c.Order });

        modelBuilder.Entity<RubricLevel>()
            .HasIndex(l => new { l.CriterionId, l.Order });

        // Deleting a user takes their rubrics, and through the cascade above
        // the criteria and levels, with it
        modelBuilder.Entity<Rubric>()
            .HasOne(r => r.UploadedBy)
            .WithMany()
            .HasForeignKey(r => r.UploadedByUserId)
            .OnDelete(DeleteBehavior.Cascade);
    }

}

