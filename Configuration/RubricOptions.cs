namespace GradrTab.Configuration;

// Bound from the "Rubric" section of appsettings.
public class RubricOptions
{
    public const string SectionName = "Rubric";

    // Upper bound on the number of criteria in one rubric
    public int MaxCriteriaPerRubric { get; set; } = 50;

    // Upper bound on the number of scoring bands inside one criterion
    public int MaxLevelsPerCriterion { get; set; } = 10;
}
