using System.Globalization;
using System.Text.Json;
using FluentValidation;

namespace FieldOps.Application.Features.PublicRequests;

internal static class PublicSlugs
{
    // Compared lowercased (BR-01); a value that is not a valid slug simply never matches.
    public static string Normalize(string? slug) => (slug ?? string.Empty).Trim().ToLowerInvariant();
}

/// <summary>Anonymous form configuration for a public slug (FR-01, FR-02).</summary>
public sealed class GetPublicServiceRequestFormHandler(IPublicServiceRequestStore store)
{
    public Task<PublicServiceRequestForm?> HandleAsync(string? slug, CancellationToken cancellationToken) =>
        store.FindAcceptingFormAsync(PublicSlugs.Normalize(slug), cancellationToken);
}

/// <summary>
/// Validates and stores an anonymous public service request, then sends the
/// confirmation email after the commit (FR-07 to FR-13).
/// </summary>
public sealed class SubmitPublicServiceRequestHandler(
    IPublicServiceRequestStore store,
    IValidator<PublicSubmissionValidationInput> validator,
    IPublicRequestConfirmationSender confirmationSender,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<SubmitPublicServiceRequestResult> HandleAsync(
        SubmitPublicServiceRequestCommand command,
        CancellationToken cancellationToken)
    {
        var form = await store.FindAcceptingFormAsync(PublicSlugs.Normalize(command.Slug), cancellationToken);

        if (form is null)
        {
            return new SubmitPublicServiceRequestResult.NotFound();
        }

        // Honeypot (BR-17): generic failure before any other rule.
        if (!string.IsNullOrEmpty(command.Website))
        {
            return new SubmitPublicServiceRequestResult.Honeypot();
        }

        var now = timeProvider.GetUtcNow();
        var timeZone = ResolveTimeZone(form.Timezone);
        var today = AvailabilityWindowCalculator.Today(now, timeZone);

        var validation = await validator.ValidateAsync(
            new PublicSubmissionValidationInput(command, today, form),
            cancellationToken);

        if (!validation.IsValid)
        {
            return Invalid(validation.Errors
                .GroupBy(error => error.PropertyName, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(error => error.ErrorMessage).Distinct().ToArray(),
                    StringComparer.Ordinal));
        }

        var submission = BuildSubmission(command, form, timeZone, now);
        var outcome = await store.SubmitAsync(submission, cancellationToken);

        if (outcome is not PublicSubmissionOutcome.Created created)
        {
            return Invalid(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["service.categoryId"] = [SubmitPublicServiceRequestValidator.CategoryMessage],
                ["service.serviceId"] = [SubmitPublicServiceRequestValidator.ServiceMessage],
            });
        }

        await confirmationSender.SendAsync(
            new PublicRequestConfirmation(
                created.RequestId,
                submission.Email,
                submission.FirstName,
                form.OrganizationName,
                created.RequestNumber),
            cancellationToken);

        return new SubmitPublicServiceRequestResult.Created(created.RequestNumber);
    }

    private static SubmitPublicServiceRequestResult Invalid(IReadOnlyDictionary<string, string[]> errors) =>
        new SubmitPublicServiceRequestResult.Invalid(errors);

    internal static PublicSubmission BuildSubmission(
        SubmitPublicServiceRequestCommand command,
        PublicServiceRequestForm form,
        TimeZoneInfo timeZone,
        DateTimeOffset now)
    {
        var contact = command.Contact;
        var property = command.Property;
        var service = command.Service;
        var availability = command.Availability;
        var timeWindow = availability.TimeWindow!;

        DateTimeOffset? preferredStart = null;
        DateTimeOffset? preferredEnd = null;
        string? preferredDate = null;

        if (availability.DateMode == "date"
            && SubmitPublicServiceRequestValidator.TryParseDate(availability.PreferredDate!.Trim(), out var date))
        {
            (var start, var end) = AvailabilityWindowCalculator.Calculate(date, timeWindow, timeZone);
            preferredStart = start;
            preferredEnd = end;
            preferredDate = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        var notes = NullIfEmpty(availability.SchedulingNotes);

        var availabilityJson = JsonSerializer.Serialize(
            new
            {
                dateMode = availability.DateMode,
                preferredDate,
                timeWindow,
                schedulingNotes = notes,
            },
            JsonOptions);

        return new PublicSubmission(
            form.OrganizationId,
            form.MainBranchId,
            contact.FirstName!.Trim(),
            contact.LastName!.Trim(),
            contact.Email!.Trim(),
            contact.Phone!.Trim(),
            contact.PrefersEmail,
            contact.PrefersSms,
            property.PropertyType!,
            property.AddressLine1!.Trim(),
            NullIfEmpty(property.AddressLine2),
            property.City!.Trim(),
            property.State!.Trim(),
            property.PostalCode!.Trim(),
            NullIfEmpty(property.AccessInstructions),
            service.CategoryId!.Value,
            service.NotSure ? null : service.ServiceId,
            service.Description!.Trim(),
            service.Urgency!,
            service.HasActiveDamage,
            availabilityJson,
            preferredStart,
            preferredEnd,
            now,
            command.Attachments
                .Select(file => new PublicSubmissionAttachment(
                    AttachmentContentInspector.SanitizeFileName(file.FileName),
                    AttachmentContentInspector.Inspect(file.FileName, file.Content).MimeType!,
                    file.Content))
                .ToList());
    }

    private static string? NullIfEmpty(string? value)
    {
        var trimmed = value?.Trim();

        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static TimeZoneInfo ResolveTimeZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}
