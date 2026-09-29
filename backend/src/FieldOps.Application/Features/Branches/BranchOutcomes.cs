namespace FieldOps.Application.Features.Branches;

/// <summary>Outcome of <see cref="IBranchStore.DeactivateAsync"/> (FR-09, BR-06, BR-08).</summary>
public enum DeactivateBranchOutcome
{
    NotFound,
    NoOp,
    LastActiveConflict,
    MainBranchConflict,
    Changed,
}

/// <summary>Outcome of <see cref="IBranchStore.SetMainAsync"/> (FR-15, BR-11).</summary>
public enum SetMainBranchOutcome
{
    NotFound,
    InactiveConflict,
    NoOp,
    Changed,
}

/// <summary>Outcome of <see cref="IBranchStore.ReactivateAsync"/> (FR-09, BR-08).</summary>
public enum ReactivateBranchOutcome
{
    NotFound,
    NoOp,
    Changed,
}
