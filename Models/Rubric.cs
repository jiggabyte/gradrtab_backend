using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GradrTab.Models;

// A grading rubric submitted by the frontend. The client supplies its own
// string key (the "id" in the payload) which stays unique per user so a
// frontend can address a rubric without knowing the database id.
public class Rubric
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    // The "id" from the submitted payload, for example rub_essay
    [Required]
    [MaxLength(100)]
    public string RubricKey { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    // Sum of the highest level of every criterion, computed on save
    public int MaxPoints { get; set; }

    public int CriterionCount { get; set; }

    // The authenticated user that submitted the rubric
    public Guid? UploadedByUserId { get; set; }

    [ForeignKey(nameof(UploadedByUserId))]
    public User? UploadedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<RubricCriterion> Criteria { get; set; } = new List<RubricCriterion>();
}
