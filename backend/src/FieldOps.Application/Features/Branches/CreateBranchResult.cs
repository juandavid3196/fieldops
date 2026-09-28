namespace FieldOps.Application.Features.Branches;

public abstract record CreateBranchResult
{
    private CreateBranchResult()
    {
    }

    public sealed record Succeeded(BranchDetailView Branch) : CreateBranchResult;

    public sealed record Invalid(IReadOnlyDictionary<string, string[]> Errors) : CreateBranchResult;

    public sealed record DuplicateCode : CreateBranchResult;

    public sealed record LimitReached : CreateBranchResult;
}
