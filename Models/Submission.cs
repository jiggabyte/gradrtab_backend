using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GradrTab.DTOs;

namespace GradrTab.Models;


public class Submission
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? RubricId{ get; set; }

    [ForeignKey(nameof(RubricId))]
    public Rubric? Rubric { get; set; }

    public string StudentId { get; set; } = string.Empty;

    public string StudentName { get; set; } = string.Empty;

    public Guid? DocumentId { get; set; }

    [ForeignKey(nameof(DocumentId))]
    public Document? Document { get; set; }

    [Column(TypeName = "jsonb")]
    public string? Evaluation { get; set; }

    public GradeState GradingStatus { get; set; } = GradeState.NotStarted;

    public string FailureReason { get; set; } = string.Empty;

    // The authenticated user that made the submission
    public Guid? UploadedByUserId { get; set; }

    [ForeignKey(nameof(UploadedByUserId))]
    public User? UploadedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

}
