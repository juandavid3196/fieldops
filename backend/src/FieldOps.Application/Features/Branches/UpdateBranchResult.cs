namespace FieldOps.Application.Features.Branches;

public abstract record UpdateBranchResult
{
    private UpdateBranchResult()
    {
    }

    public sealed record Succeeded(BranchDetailView Branch) : UpdateBranchResult;

    public sealed record Invalid(IReadOnlyDictionary<string, string[]> Errors) : UpdateBranchResult;

    public sealed record NotFound : UpdateBranchResult;

    public sealed record DuplicateCode : UpdateBranchResult;

    public sealed record Stale : UpdateBranchResult;
}
