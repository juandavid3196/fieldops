using System.Globalization;
using System.Text.Json;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Domain.Catalog;
using FieldOps.Domain.Customers;
using FieldOps.Domain.Notifications;
using FieldOps.Domain.Requests;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FieldOps.Infrastructure.Persistence;

internal sealed partial class ServiceRequestStore
{
    private const int GuestNameMaxLength = 180;

    public async Task<RequestCreationOutcome> CreateInternalAsync(
        RequestActor actor, InternalRequestInput input, CancellationToken cancellationToken)
    {
        var organizationId = actor.OrganizationId;
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        var customerQuery = dbContext.Customers.AsNoTracking()
            .Where(candidate => candidate.Id == input.CustomerId
                && candidate.OrganizationId == organizationId
                && candidate.IsActive);

        if (!actor.Scope.All)
        {
            var ids = actor.Scope.BranchIds.ToArray();
            customerQuery = customerQuery.Where(candidate => ids.Contains(candidate.BranchId));
        }

        var customer = await customerQuery
            .Select(candidate => new { candidate.Id, candidate.Type })
            .SingleOrDefaultAsync(cancellationToken);

        ContactSnapshot? contact = null;
        PropertySnapshot? property = null;

        if (customer is null)
        {
            errors["customerId"] = [ServiceRequestMessages.CustomerNotAllowed];
        }
        else
        {
            contact = await dbContext.CustomerContacts.AsNoTracking()
                .Where(candidate => candidate.Id == input.ContactId
                    && candidate.OrganizationId == organizationId
                    && candidate.CustomerId == customer.Id
                    && candidate.IsActive)
                .Select(candidate => new ContactSnapshot(candidate.FirstName, candidate.LastName, candidate.Email, candidate.Phone))
                .SingleOrDefaultAsync(cancellationToken);

            if (contact is null)
            {
                errors["contactId"] = [ServiceRequestMessages.ContactNotAllowed];
            }

            property = await dbContext.Properties.AsNoTracking()
                .Where(candidate => candidate.Id == input.PropertyId
                    && candidate.OrganizationId == organizationId
                    && candidate.CustomerId == customer.Id
                    && candidate.IsActive)
                .Select(candidate => new PropertySnapshot(
                    candidate.BranchId,
                    candidate.AddressLine1,
                    candidate.AddressLine2,
                    candidate.City,
                    candidate.StateRegion,
                    candidate.PostalCode,
                    candidate.CountryCode))
                .SingleOrDefaultAsync(cancellationToken);

            if (property is null)
            {
                errors["propertyId"] = [ServiceRequestMessages.PropertyNotAllowed];
            }
            else if (property.BranchId is { } propertyBranch && !actor.Scope.Contains(propertyBranch))
            {
                errors["propertyId"] = [ServiceRequestMessages.PropertyBranchNotAllowed];
            }
        }

        var categoryActive = await dbContext.ServiceCategories.AsNoTracking().AnyAsync(
            category => category.Id == input.CategoryId && category.OrganizationId == organizationId && category.IsActive,
            cancellationToken);

        if (!categoryActive)
        {
            errors["categoryId"] = [FieldOps.Application.Features.PublicRequests.SubmitPublicServiceRequestValidator.CategoryMessage];
        }

        if (input.ServiceId is { } serviceId
            && !await dbContext.CatalogItems.AsNoTracking().AnyAsync(
                item => item.Id == serviceId
                    && item.OrganizationId == organizationId
                    && item.IsActive
                    && item.Type == CatalogItemType.Service
                    && item.CategoryId == input.CategoryId,
                cancellationToken))
        {
            errors["serviceId"] = [FieldOps.Application.Features.PublicRequests.SubmitPublicServiceRequestValidator.ServiceMessage];
        }

        if (errors.Count > 0)
        {
            return new RequestCreationOutcome.Invalid(errors);
        }

        var propertyData = property!;
        var contactData = contact!;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Raw SQL: EF has no declarative row-lock API. Concurrent creations serialize here, so request
        // numbers are consecutive; an aborted transaction never consumes a number.
        var locked = await dbContext.Database
            .SqlQuery<long>(
                $"""
                SELECT next_request_number AS "Value" FROM organizations
                WHERE id = {organizationId} AND is_active = true
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);

        if (locked.Count != 1)
        {
            throw new InvalidOperationException("The organization is no longer available.");
        }

        var requestNumber = locked[0];
        var requestPrefix = await dbContext.Organizations.AsNoTracking()
            .Where(organization => organization.Id == organizationId)
            .Select(organization => organization.RequestPrefix)
            .SingleAsync(cancellationToken);

        var guestName = FullName(contactData.FirstName, contactData.LastName);

        if (guestName.Length > GuestNameMaxLength)
        {
            guestName = guestName[..GuestNameMaxLength].TrimEnd();
        }

        var request = ServiceRequest.Create(
            organizationId,
            requestNumber,
            input.Description,
            input.PreferredStart,
            input.PreferredEnd,
            customerId: input.CustomerId,
            contactId: input.ContactId,
            propertyId: input.PropertyId,
            categoryId: input.CategoryId,
            catalogItemId: input.ServiceId,
            guestName: guestName,
            guestEmail: contactData.Email,
            guestPhone: contactData.Phone,
            serviceAddress: ServiceAddressJson(propertyData, customer!.Type),
            urgency: input.Urgency,
            hasActiveDamage: input.HasActiveDamage,
            availabilityPreferences: input.AvailabilityPreferencesJson,
            consentAt: null,
            source: "internal",
            branchId: propertyData.BranchId);
        dbContext.ServiceRequests.Add(request);

        dbContext.RequestStatusHistories.Add(
            RequestStatusHistory.Create(organizationId, request.Id, null, RequestStatus.New, actor.UserId));

        // BR-20: no names, email, phone, address, description or file names.
        dbContext.AuditLogs.Add(AuditLog.Create(
            organizationId,
            "service_request.created",
            AuditEntityType,
            actor.UserId,
            request.Id,
            request.BranchId,
            actor.IpAddress,
            afterData: Serialize(new Dictionary<string, object?>
            {
                ["requestNumber"] = RequestCardRules.DisplayNumber(requestPrefix, requestNumber),
                ["status"] = "new",
                ["source"] = request.Source,
                ["urgency"] = request.Urgency,
                ["hasActiveDamage"] = request.HasActiveDamage,
            })));

        await dbContext.Organizations
            .Where(organization => organization.Id == organizationId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(organization => organization.NextRequestNumber, requestNumber + 1),
                cancellationToken);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException postgres)
        {
            // The database detail can quote the failing row (personal data): only state and constraint travel.
            throw new InvalidOperationException(
                $"The internal service request could not be saved (SqlState {postgres.SqlState}, constraint {postgres.ConstraintName}).");
        }

        await transaction.CommitAsync(cancellationToken);

        var detail = await GetDetailAsync(organizationId, actor.Scope, request.Id, cancellationToken)
            ?? throw new InvalidOperationException("The request is no longer available.");

        return new RequestCreationOutcome.Created(detail);
    }

    private static string ServiceAddressJson(PropertySnapshot property, CustomerType customerType) =>
        JsonSerializer.Serialize(
            new
            {
                line1 = property.AddressLine1,
                line2 = property.AddressLine2,
                city = property.City,
                state = property.StateRegion,
                postalCode = property.PostalCode,
                countryCode = property.CountryCode,
                propertyType = customerType == CustomerType.Company ? "business" : "home",
            },
            JsonOptions);

    private sealed record ContactSnapshot(string FirstName, string? LastName, string? Email, string? Phone);

    private sealed record PropertySnapshot(
        Guid? BranchId,
        string AddressLine1,
        string? AddressLine2,
        string City,
        string? StateRegion,
        string? PostalCode,
        string CountryCode);
}
