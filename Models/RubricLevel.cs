using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GradrTab.Models;

// One band of a criterion, for example "Strong" worth 40 points.
public class RubricLevel
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid CriterionId { get; set; }

    [ForeignKey(nameof(CriterionId))]
    public RubricCriterion? Criterion { get; set; }

    [Required]
    [MaxLength(100)]
    public string Label { get; set; } = string.Empty;

    // Points awarded when this level is chosen, must be greater than zero
    public int Points { get; set; }

    [MaxLength(1000)]
    public string? Description { get; set; }

    public int Order { get; set; }
}
