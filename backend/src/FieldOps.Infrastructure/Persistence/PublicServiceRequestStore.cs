using System.Globalization;
using System.Text.Json;
using FieldOps.Application.Features.PublicRequests;
using FieldOps.Domain.Catalog;
using FieldOps.Domain.Customers;
using FieldOps.Domain.Notifications;
using FieldOps.Domain.Requests;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FieldOps.Infrastructure.Persistence;

internal sealed class PublicServiceRequestStore(FieldOpsDbContext dbContext) : IPublicServiceRequestStore
{
    public const string CreatedAuditAction = "service_request.created";

    public const string AuditEntityType = "service_request";

    private const int GuestNameMaxLength = 180;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<PublicServiceRequestForm?> FindAcceptingFormAsync(
        string slug, CancellationToken cancellationToken)
    {
        var organization = await dbContext.Organizations
            .AsNoTracking()
            .Where(candidate => candidate.PublicSlug == slug && candidate.IsActive)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.Name,
                candidate.Phone,
                candidate.Website,
                candidate.RequestPrefix,
                candidate.Timezone,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (organization is null)
        {
            return null;
        }

        var mainBranchId = await dbContext.Branches
            .AsNoTracking()
            .Where(branch => branch.OrganizationId == organization.Id && branch.IsMain && branch.IsActive)
            .Select(branch => (Guid?)branch.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (mainBranchId is null)
        {
            return null;
        }

        var categories = await dbContext.ServiceCategories
            .AsNoTracking()
            .Where(category => category.OrganizationId == organization.Id && category.IsActive)
            .Select(category => new { category.Id, category.Name })
            .ToListAsync(cancellationToken);

        var services = await dbContext.CatalogItems
            .AsNoTracking()
            .Where(item => item.OrganizationId == organization.Id
                && item.IsActive
                && item.Type == CatalogItemType.Service
                && item.CategoryId != null)
            .Select(item => new { item.Id, item.Name, CategoryId = item.CategoryId!.Value })
            .ToListAsync(cancellationToken);

        var servicesByCategory = services
            .GroupBy(service => service.CategoryId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var formCategories = categories
            .Where(category => servicesByCategory.ContainsKey(category.Id))
            .OrderBy(category => category.Name, StringComparer.OrdinalIgnoreCase)
            .Select(category => new PublicFormCategory(
                category.Id,
                category.Name,
                servicesByCategory[category.Id]
                    .OrderBy(service => service.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(service => new PublicFormService(service.Id, service.Name))
                    .ToList()))
            .ToList();

        if (formCategories.Count == 0)
        {
            return null;
        }

        return new PublicServiceRequestForm(
            organization.Id,
            mainBranchId.Value,
            organization.Name,
            organization.Phone,
            organization.Website,
            organization.RequestPrefix,
            organization.Timezone,
            formCategories);
    }

    public async Task<PublicSubmissionOutcome> SubmitAsync(
        PublicSubmission submission, CancellationToken cancellationToken)
    {
        var organizationId = submission.OrganizationId;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Raw SQL: EF has no declarative row-lock API. Concurrent submissions
        // for the organization serialize here, so numbers are consecutive (BR-11).
        var locked = await dbContext.Database
            .SqlQuery<long>(
                $"""
                SELECT next_request_number FROM organizations
                WHERE id = {organizationId} AND is_active = true
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);

        if (locked.Count != 1)
        {
            throw new InvalidOperationException("The organization is no longer available.");
        }

        var requestNumber = locked[0];

        if (!await CatalogIsValidAsync(submission, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);

            return new PublicSubmissionOutcome.CatalogChanged();
        }

        var requestPrefix = await dbContext.Organizations
            .AsNoTracking()
            .Where(organization => organization.Id == organizationId)
            .Select(organization => organization.RequestPrefix)
            .SingleAsync(cancellationToken);

        var (customerId, contactId, isPrimaryProperty) = await ResolveCustomerAsync(submission, cancellationToken);

        var property = Property.Create(
            organizationId,
            customerId,
            submission.PropertyType == "business" ? "Business" : "Home",
            submission.AddressLine1,
            submission.City,
            "US",
            branchId: null,
            stateRegion: submission.State,
            postalCode: submission.PostalCode,
            isPrimary: isPrimaryProperty,
            addressLine2: submission.AddressLine2,
            accessInstructions: submission.AccessInstructions);
        dbContext.Properties.Add(property);

        var guestName = GuestName(submission);

        var request = ServiceRequest.Create(
            organizationId,
            requestNumber,
            submission.Description,
            submission.PreferredStart,
            submission.PreferredEnd,
            customerId,
            contactId,
            property.Id,
            submission.CategoryId,
            submission.ServiceId,
            guestName,
            submission.Email,
            submission.Phone,
            ServiceAddressJson(submission),
            submission.Urgency,
            submission.HasActiveDamage,
            submission.AvailabilityPreferencesJson,
            submission.ConsentAt);
        dbContext.ServiceRequests.Add(request);

        foreach (var attachment in submission.Attachments)
        {
            dbContext.RequestAttachments.Add(RequestAttachment.Create(
                organizationId, request.Id, attachment.FileName, attachment.MimeType, attachment.Content));
        }

        dbContext.RequestStatusHistories.Add(
            RequestStatusHistory.Create(organizationId, request.Id, null, RequestStatus.New));

        var displayNumber = string.Create(CultureInfo.InvariantCulture, $"{requestPrefix}-{requestNumber}");

        // BR-15: no names, email, phone, address, description, notes or file names.
        dbContext.AuditLogs.Add(AuditLog.Create(
            organizationId,
            CreatedAuditAction,
            AuditEntityType,
            entityId: request.Id,
            afterData: JsonSerializer.Serialize(
                new
                {
                    requestNumber = displayNumber,
                    status = "new",
                    source = request.Source,
                    urgency = request.Urgency,
                    hasActiveDamage = request.HasActiveDamage,
                    attachmentCount = submission.Attachments.Count,
                },
                JsonOptions)));

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
            // The database detail can quote the failing row (personal data), so
            // only the SQL state and constraint name travel to the logs.
            throw new InvalidOperationException(
                $"The public service request could not be saved (SqlState {postgres.SqlState}, constraint {postgres.ConstraintName}).");
        }

        await transaction.CommitAsync(cancellationToken);

        return new PublicSubmissionOutcome.Created(request.Id, displayNumber);
    }

    private async Task<bool> CatalogIsValidAsync(PublicSubmission submission, CancellationToken cancellationToken)
    {
        var categoryActive = await dbContext.ServiceCategories.AnyAsync(
            category => category.Id == submission.CategoryId
                && category.OrganizationId == submission.OrganizationId
                && category.IsActive,
            cancellationToken);

        if (!categoryActive || submission.ServiceId is not { } serviceId)
        {
            return categoryActive;
        }

        return await dbContext.CatalogItems.AnyAsync(
            item => item.Id == serviceId
                && item.OrganizationId == submission.OrganizationId
                && item.IsActive
                && item.Type == CatalogItemType.Service
                && item.CategoryId == submission.CategoryId,
            cancellationToken);
    }

    // BR-09: exactly one active contact of an active customer reuses it
    // unchanged; zero or several create a new customer and primary contact.
    private async Task<(Guid CustomerId, Guid ContactId, bool IsPrimaryProperty)> ResolveCustomerAsync(
        PublicSubmission submission, CancellationToken cancellationToken)
    {
        var organizationId = submission.OrganizationId;
        var email = submission.Email.ToLowerInvariant();

        var matches = await dbContext.CustomerContacts
            .AsNoTracking()
            .Where(contact => contact.OrganizationId == organizationId
                && contact.IsActive
                && contact.Email != null
                && contact.Email.ToLower() == email)
            .Join(
                dbContext.Customers.Where(customer => customer.OrganizationId == organizationId && customer.IsActive),
                contact => contact.CustomerId,
                customer => customer.Id,
                (contact, customer) => new { ContactId = contact.Id, CustomerId = customer.Id })
            .Take(2)
            .ToListAsync(cancellationToken);

        if (matches.Count == 1)
        {
            var match = matches[0];
            var hasPrimary = await dbContext.Properties.AnyAsync(
                property => property.OrganizationId == organizationId
                    && property.CustomerId == match.CustomerId
                    && property.IsActive
                    && property.IsPrimary,
                cancellationToken);

            return (match.CustomerId, match.ContactId, !hasPrimary);
        }

        var customer = Customer.Create(
            organizationId,
            submission.MainBranchId,
            submission.PropertyType == "business" ? CustomerType.Company : CustomerType.Person,
            GuestName(submission),
            submission.Email,
            submission.Phone);
        dbContext.Customers.Add(customer);

        var newContact = CustomerContact.CreatePrimary(
            organizationId,
            customer.Id,
            submission.FirstName,
            submission.LastName,
            submission.Email,
            submission.Phone,
            title: null,
            submission.PrefersEmail,
            submission.PrefersSms);
        dbContext.CustomerContacts.Add(newContact);

        return (customer.Id, newContact.Id, true);
    }

    // First + last names may total more than the 180-character column.
    private static string GuestName(PublicSubmission submission)
    {
        var name = $"{submission.FirstName} {submission.LastName}";

        return name.Length <= GuestNameMaxLength ? name : name[..GuestNameMaxLength].TrimEnd();
    }

    private static string ServiceAddressJson(PublicSubmission submission) =>
        JsonSerializer.Serialize(
            new
            {
                line1 = submission.AddressLine1,
                line2 = submission.AddressLine2,
                city = submission.City,
                state = submission.State,
                postalCode = submission.PostalCode,
                countryCode = "US",
                propertyType = submission.PropertyType,
            },
            JsonOptions);
}
