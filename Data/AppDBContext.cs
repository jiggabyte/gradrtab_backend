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

    public DbSet<Submission> Submissions { get; set; }

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

        // The rubric columns are sized here, the model itself carries no
        // annotations of its own
        modelBuilder.Entity<Rubric>()
            .Property(r => r.CourseName)
            .HasMaxLength(200);

        modelBuilder.Entity<Rubric>()
            .Property(r => r.CourseId)
            .HasMaxLength(100);

        // The rubric itself can be long, so it is not bound by a varchar limit
        modelBuilder.Entity<Rubric>()
            .Property(r => r.RubricContent)
            .HasColumnType("text");

        modelBuilder.Entity<Rubric>()
            .HasIndex(r => r.CreatedAt);

        // Deleting a user takes their rubrics with it
        modelBuilder.Entity<Rubric>()
            .HasOne(r => r.UploadedBy)
            .WithMany()
            .HasForeignKey(r => r.UploadedByUserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Store the grading state as a readable string instead of an integer
        modelBuilder.Entity<Submission>()
            .Property(s => s.GradingStatus)
            .HasConversion<string>()
            .HasMaxLength(20);

        // A submission outlives the rubric or the document it points at, the
        // reference is simply cleared when either one goes away
        modelBuilder.Entity<Submission>()
            .HasOne(s => s.Rubric)
            .WithMany()
            .HasForeignKey(s => s.RubricId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Submission>()
            .HasOne(s => s.Document)
            .WithMany()
            .HasForeignKey(s => s.DocumentId)
            .OnDelete(DeleteBehavior.SetNull);

        // Deleting a user takes their submissions with it
        modelBuilder.Entity<Submission>()
            .HasOne(s => s.UploadedBy)
            .WithMany()
            .HasForeignKey(s => s.UploadedByUserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Submission>()
            .HasIndex(s => s.CreatedAt);

        // Listing the submissions of one rubric is the usual way in
        modelBuilder.Entity<Submission>()
            .HasIndex(s => new { s.RubricId, s.CreatedAt });

        // Supports looking up everything one student handed in
        modelBuilder.Entity<Submission>()
            .HasIndex(s => new { s.StudentId, s.CreatedAt });
    }

}

