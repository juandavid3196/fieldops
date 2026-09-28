namespace FieldOps.Application.Features.Branches;

/// <summary>Outcome of <see cref="IBranchStore.DeactivateAsync"/> (FR-09, BR-06, BR-08).</summary>
public enum DeactivateBranchOutcome
{
    NotFound,
    NoOp,
    LastActiveConflict,
    Changed,
}

/// <summary>Outcome of <see cref="IBranchStore.ReactivateAsync"/> (FR-09, BR-08).</summary>
public enum ReactivateBranchOutcome
{
    NotFound,
    NoOp,
    Changed,
}
