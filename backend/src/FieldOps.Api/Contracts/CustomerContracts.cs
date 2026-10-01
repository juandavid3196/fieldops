using FieldOps.Application.Features.Customers;

namespace FieldOps.Api.Contracts;

public sealed record CustomerContactRequest(
    string? FirstName,
    string? LastName,
    string? Title,
    string? Email,
    string? Phone,
    bool PrefersEmail,
    bool PrefersSms);

public sealed record CustomerPropertyRequest(
    string? AddressLine1,
    string? City,
    string? StateRegion,
    string? PostalCode);

/// <summary>
/// Body of POST/PUT /customers (BR-10 to BR-13). No organization identifier is accepted; branch and tag
/// ids arrive as text and are verified against the session organization and scope. The type is required on both and may change on PUT.
/// </summary>
public sealed record CustomerRequest(
    string? Type,
    string? CompanyName,
    CustomerContactRequest? Contact,
    CustomerPropertyRequest? Property,
    string? ServiceInstructions,
    string? InternalNote,
    string? BranchId,
    string[]? TagIds)
{
    public CustomerWriteInput ToInput() =>
        new(
            new CustomerInput(
                Type,
                CompanyName,
                Contact?.FirstName,
                Contact?.LastName,
                Contact?.Title,
                Contact?.Email,
                Contact?.Phone,
                Contact?.PrefersEmail ?? false,
                Contact?.PrefersSms ?? false,
                Property?.AddressLine1,
                Property?.City,
                Property?.StateRegion,
                Property?.PostalCode,
                ServiceInstructions,
                InternalNote),
            BranchId,
            TagIds);
}

/// <summary>Query of GET /customers (BR-06 to BR-08); strings so bad input maps to a 400 key.</summary>
public sealed class CustomerListRequest
{
    public string? Tab { get; init; }

    public string? Search { get; init; }

    public string? Type { get; init; }

    public string? BranchId { get; init; }

    public string[]? TagIds { get; init; }

    public string? BalanceStatus { get; init; }

    public string? Sort { get; init; }

    public string? Page { get; init; }

    public CustomerListQuery ToQuery() => new(Tab, Search, Type, BranchId, TagIds, BalanceStatus, Sort, Page);
}

public sealed record DuplicateCheckRequest(string? Email, string? Phone, string? ExcludeCustomerId);

public sealed record CreateCustomerTagRequest(string? Name);

public sealed record CustomerCreatedResponse(Guid Id);

public sealed record CustomerImportResponse(int ImportedCount);

/// <summary>
/// Body of POST/PUT /customers/{id}/properties[/{propertyId}] (BR-05, BR-06). The branch id arrives as
/// text and is verified against the session organization and scope.
/// </summary>
public sealed record CustomerPropertyWriteRequest(
    string? Name,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? StateRegion,
    string? PostalCode,
    string? BranchId,
    string? ServiceInstructions)
{
    public CustomerPropertyInput ToInput() =>
        new(Name, AddressLine1, AddressLine2, City, StateRegion, PostalCode, BranchId, ServiceInstructions);
}

/// <summary>Body of POST /customers/{id}/notes (BR-16).</summary>
public sealed record CustomerNoteRequest(string? Note);
