using FieldOps.Api.Authorization;
using FieldOps.Application.Features.BillingReview;
using FieldOps.Application.Features.InvoicePayments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FieldOps.Api.Controllers;

/// <summary>
/// The invoices and payments hub reads (invoices-payments-management FR-01 to FR-05). No endpoint accepts an organization
/// identifier: the organization and branch scope come from the session membership, and all of them use the billing read
/// policy. The literal templates never match <c>invoices/{invoiceId:guid}</c>. Every response is no-store.
/// </summary>
public sealed class InvoiceHubController(
    GetHubOptionsHandler optionsHandler,
    SearchHubCustomersHandler customersHandler,
    GetHubOverviewHandler overviewHandler,
    ListHubInvoicesHandler invoicesHandler,
    ListHubPaymentsHandler paymentsHandler,
    ExportHubInvoicesHandler exportInvoicesHandler,
    ExportHubPaymentsHandler exportPaymentsHandler,
    IAuthorizationService authorization) : WorkOrderControllerBase
{
    [HttpGet("invoices/options")]
    [Authorize(Policy = BillingReviewPolicies.Read)]
    [ProducesResponseType<HubOptions>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Options(CancellationToken cancellationToken)
    {
        var failure = Begin(out var ticket);

        if (failure is not null)
        {
            return failure;
        }

        var canAct = (await authorization.AuthorizeAsync(User, BillingReviewPolicies.Act)).Succeeded;

        return Ok(await optionsHandler.HandleAsync(Call(ticket), canAct, cancellationToken));
    }

    [HttpGet("invoices/customers")]
    [Authorize(Policy = BillingReviewPolicies.Read)]
    [ProducesResponseType<IReadOnlyList<NamedOption>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Customers(string? search, CancellationToken cancellationToken)
    {
        var failure = Begin(out var ticket);

        return failure ?? Respond(await customersHandler.HandleAsync(Call(ticket), search, cancellationToken), value => Ok(value));
    }

    [HttpGet("invoices/overview")]
    [Authorize(Policy = BillingReviewPolicies.Read)]
    [ProducesResponseType<HubOverview>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Overview(string? branchId, CancellationToken cancellationToken)
    {
        var failure = Begin(out var ticket);

        return failure ?? Respond(await overviewHandler.HandleAsync(Call(ticket), branchId, cancellationToken), value => Ok(value));
    }

    [HttpGet("invoices")]
    [Authorize(Policy = BillingReviewPolicies.Read)]
    [ProducesResponseType<InvoicePage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Invoices(
        string? search,
        string? from,
        string? to,
        string? status,
        string? customerId,
        string? branchId,
        string? page,
        CancellationToken cancellationToken)
    {
        var failure = Begin(out var ticket);

        if (failure is not null)
        {
            return failure;
        }

        var canAct = (await authorization.AuthorizeAsync(User, BillingReviewPolicies.Act)).Succeeded;

        return Respond(
            await invoicesHandler.HandleAsync(
                Call(ticket), new InvoiceQueryText(search, from, to, status, customerId, branchId, page), canAct, cancellationToken),
            value => Ok(value));
    }

    [HttpGet("invoices/export")]
    [Authorize(Policy = BillingReviewPolicies.Read)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ExportInvoices(
        string? search, string? from, string? to, string? status, string? customerId, string? branchId, CancellationToken cancellationToken)
    {
        var failure = Begin(out var ticket);

        return failure ?? Respond(
            await exportInvoicesHandler.HandleAsync(
                Call(ticket), new InvoiceQueryText(search, from, to, status, customerId, branchId, null), cancellationToken),
            File);
    }

    [HttpGet("invoices/payments")]
    [Authorize(Policy = BillingReviewPolicies.Read)]
    [ProducesResponseType<PaymentPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Payments(
        string? search, string? from, string? to, string? method, string? branchId, string? page, CancellationToken cancellationToken)
    {
        var failure = Begin(out var ticket);

        return failure ?? Respond(
            await paymentsHandler.HandleAsync(Call(ticket), new PaymentQueryText(search, from, to, method, branchId, page), cancellationToken),
            value => Ok(value));
    }

    [HttpGet("invoices/payments/export")]
    [Authorize(Policy = BillingReviewPolicies.Read)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ExportPayments(
        string? search, string? from, string? to, string? method, string? branchId, CancellationToken cancellationToken)
    {
        var failure = Begin(out var ticket);

        return failure ?? Respond(
            await exportPaymentsHandler.HandleAsync(Call(ticket), new PaymentQueryText(search, from, to, method, branchId, null), cancellationToken),
            File);
    }

    private IActionResult File(BillingCsvFile file)
    {
        Response.Headers.XContentTypeOptions = "nosniff";

        return File(file.Content, "text/csv; charset=utf-8", file.FileName);
    }

    private IActionResult Respond<T>(BillingOutcome<T> outcome, Func<T, IActionResult> success) =>
        outcome switch
        {
            BillingOutcome<T>.Succeeded succeeded => success(succeeded.Value),
            BillingOutcome<T>.Invalid invalid => new ObjectResult(new ValidationProblemDetails(
                new Dictionary<string, string[]>(invalid.Errors, StringComparer.Ordinal))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
            })
            {
                StatusCode = StatusCodes.Status400BadRequest,
            },
            BillingOutcome<T>.Conflict conflict => ConflictResult(conflict.Code, conflict.Title),
            _ => NotFound(),
        };

    // Every 409 carries a machine-readable code.
    private static ObjectResult ConflictResult(string code, string title)
    {
        var problem = new ProblemDetails { Status = StatusCodes.Status409Conflict, Title = title };
        problem.Extensions["code"] = code;

        return new ObjectResult(problem) { StatusCode = StatusCodes.Status409Conflict };
    }
}
