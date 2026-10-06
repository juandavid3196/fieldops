using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Domain.Requests;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

/// <summary>Reads the latest completed assessment of a request with its findings (quote-builder BR-03); never projects photo content.</summary>
internal static class CompletedAssessmentReader
{
    public static async Task<DetailCompletedAssessment?> ReadAsync(
        FieldOpsDbContext dbContext,
        Guid organizationId,
        Guid requestId,
        TimeZoneInfo zone,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.Assessments.AsNoTracking()
            .Where(assessment => assessment.OrganizationId == organizationId
                && assessment.RequestId == requestId
                && assessment.Status == AssessmentStatus.Completed
                && assessment.CompletedAt != null)
            .OrderByDescending(assessment => assessment.CompletedAt)
            .ThenByDescending(assessment => assessment.Id)
            .Select(assessment => new
            {
                assessment.Id,
                assessment.ScheduledStart,
                assessment.CompletedAt,
                assessment.TechnicianId,
                assessment.Diagnosis,
                assessment.RecommendedScope,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        DetailFindingsTechnician? technician = null;

        if (row.TechnicianId is { } technicianId)
        {
            var profile = await dbContext.TechnicianProfiles.AsNoTracking()
                .Where(candidate => candidate.Id == technicianId && candidate.OrganizationId == organizationId)
                .Select(candidate => new { candidate.Id, candidate.FirstName, candidate.LastName })
                .SingleOrDefaultAsync(cancellationToken);

            if (profile is not null)
            {
                technician = new DetailFindingsTechnician(
                    profile.Id,
                    string.Join(' ', new[] { profile.FirstName, profile.LastName }.Where(part => !string.IsNullOrWhiteSpace(part))).Trim());
            }
        }

        var photos = await dbContext.AssessmentAttachments.AsNoTracking()
            .Where(photo => photo.OrganizationId == organizationId && photo.AssessmentId == row.Id)
            .OrderBy(photo => photo.CreatedAt)
            .ThenBy(photo => photo.Id)
            .Select(photo => new DetailPhoto(photo.Id, photo.FileName, photo.MimeType, photo.SizeBytes))
            .ToListAsync(cancellationToken);

        return new DetailCompletedAssessment(
            row.Id,
            OrganizationTime.ToZone(row.ScheduledStart, zone),
            OrganizationTime.ToZone(row.CompletedAt!.Value, zone),
            technician,
            row.Diagnosis,
            row.RecommendedScope,
            photos);
    }
}
