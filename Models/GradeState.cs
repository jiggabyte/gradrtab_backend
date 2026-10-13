namespace GradrTab.Models;

// Where a submission currently sits in the grading pipeline. A submission
// starts at NotStarted and ends up at either Complete or Failure.
public enum GradeState
{
    NotStarted,
    InProgress,
    Complete,
    Failure
}