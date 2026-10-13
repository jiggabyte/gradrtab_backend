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


    public string CourseName{ get; set; } = string.Empty;

    public string CourseId { get; set; } = string.Empty;

    public string RubricContent { get; set; } = string.Empty;

    // The authenticated user that submitted the rubric
    public Guid? UploadedByUserId { get; set; }

    [ForeignKey(nameof(UploadedByUserId))]
    public User? UploadedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

}
