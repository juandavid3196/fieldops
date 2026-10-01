using FieldOps.Application.Features.Team;

namespace FieldOps.Api.Contracts;

/// <summary>Body of POST/PUT /team/technicians (BR-15). No organization identifier is accepted.</summary>
public sealed record TechnicianRequest(
    string? FirstName,
    string? LastName,
    string? Email,
    string? Phone,
    string? EmployeeCode,
    string? BranchId,
    string? Notes)
{
    public TeamProfileInput ToInput() => new(FirstName, LastName, Email, Phone, EmployeeCode, BranchId, Notes);
}

/// <summary>Query of GET /team/technicians (BR-09, BR-10); strings so bad input maps to a 400 key.</summary>
public sealed class TechnicianListRequest
{
    public string? Search { get; init; }

    public string? BranchId { get; init; }

    public string? SkillId { get; init; }

    public string? Status { get; init; }

    public string? AccountLink { get; init; }

    public string? Sort { get; init; }

    public string? Period { get; init; }

    public string? Page { get; init; }

    public TeamListQuery ToQuery() => new(Search, BranchId, SkillId, Status, AccountLink, Sort, Period, Page);
}

public sealed record AccountLinkRequest(Guid? OrganizationUserId);
