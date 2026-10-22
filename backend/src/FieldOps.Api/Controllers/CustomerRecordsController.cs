using FieldOps.Api.Authentication;
using FieldOps.Api.Authorization;
using FieldOps.Application.Features.Customers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FieldOps.Api.Controllers;

/// <summary>
/// The related-record tabs of the customer page (customer-related-records FR-02 to FR-05, FR-09). Two stacked policies
/// give 401 then 403 (Customers read and the module read); the customer is then resolved (404) before the query is
/// validated (400). No endpoint accepts an organization identifier; every response is no-store.
/// </summary>
/// <remarks>Not an [ApiController]: query values are parsed by the Application layer.</remarks>
[Route("customers/{id:guid}")]
public sealed class CustomerRecordsController(
    ListCustomerRequestsHandler requestsHandler,
    ListCustomerQuotesHandler quotesHandler,
    ListCustomerJobsHandler jobsHandler,
    ListCustomerInvoicesHandler invoicesHandler) : ControllerBase
{
    [HttpGet("requests")]
    [Authorize(Policy = CustomerPolicies.View)]
    [Authorize(Policy = ServiceRequestPolicies.View)]
    [ProducesResponseType<CustomerRequestsPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Requests(
        Guid id,
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery] string? page,
        [FromQuery] string? pageSize,
        CancellationToken cancellationToken) =>
        ReadAsync(ticket => requestsHandler.HandleAsync(
            ticket.OrganizationId,
            ticket.MembershipId,
            id,
            new CustomerRecordQueryText(search, status, page, pageSize),
            cancellationToken));

    [HttpGet("quotes")]
    [Authorize(Policy = CustomerPolicies.View)]
    [Authorize(Policy = ServiceRequestPolicies.View)]
    [ProducesResponseType<CustomerQuotesPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Quotes(
        Guid id,
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery] string? page,
        [FromQuery] string? pageSize,
        CancellationToken cancellationToken) =>
        ReadAsync(ticket => quotesHandler.HandleAsync(
            ticket.OrganizationId,
            ticket.MembershipId,
            id,
            new CustomerRecordQueryText(search, status, page, pageSize),
            cancellationToken));

    [HttpGet("jobs")]
    [Authorize(Policy = CustomerPolicies.View)]
    [Authorize(Policy = WorkOrderPolicies.Read)]
    [ProducesResponseType<CustomerJobsPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Jobs(
        Guid id,
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery] string? page,
        [FromQuery] string? pageSize,
        CancellationToken cancellationToken) =>
        ReadAsync(ticket => jobsHandler.HandleAsync(
            ticket.OrganizationId,
            ticket.MembershipId,
            id,
            new CustomerRecordQueryText(search, status, page, pageSize),
            cancellationToken));

    [HttpGet("invoices")]
    [Authorize(Policy = CustomerPolicies.View)]
    [Authorize(Policy = BillingReviewPolicies.Read)]
    [ProducesResponseType<CustomerInvoicesPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Invoices(
        Guid id,
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery] string? page,
        [FromQuery] string? pageSize,
        CancellationToken cancellationToken) =>
        ReadAsync(ticket => invoicesHandler.HandleAsync(
            ticket.OrganizationId,
            ticket.MembershipId,
            id,
            new CustomerRecordQueryText(search, status, page, pageSize),
            cancellationToken));

    private async Task<IActionResult> ReadAsync<T>(Func<SessionTicket, Task<CustomerResult<T>>> read)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var result = await read(ticket);

        return result.Kind switch
        {
            CustomerResultKind.Succeeded => Ok(result.Value),
            CustomerResultKind.Invalid => new ObjectResult(
                new ValidationProblemDetails(new Dictionary<string, string[]>(result.Errors!, StringComparer.Ordinal))
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "One or more validation errors occurred.",
                })
            {
                StatusCode = StatusCodes.Status400BadRequest,
            },
            _ => NotFound(),
        };
    }
}
