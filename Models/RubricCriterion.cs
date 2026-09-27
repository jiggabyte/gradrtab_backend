using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GradrTab.Models;

// One scoring criterion of a rubric, for example "Evidence".
public class RubricCriterion
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid RubricId { get; set; }

    [ForeignKey(nameof(RubricId))]
    public Rubric? Rubric { get; set; }

    // The "id" of the criterion in the submitted payload, for example "thesis"
    [Required]
    [MaxLength(100)]
    public string CriterionKey { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    // Highest value found in the levels of this criterion
    public int MaxPoints { get; set; }

    // Position in the submitted payload, kept so the order survives a round trip
    public int Order { get; set; }

    public ICollection<RubricLevel> Levels { get; set; } = new List<RubricLevel>();
}
