using System.Text.Json;
using FieldOps.Application.Features.Organizations;
using FieldOps.Domain.Notifications;
using FieldOps.Domain.Organizations;

namespace FieldOps.UnitTests.Organizations;

/// <summary>BR-02/BR-05's floor checks (AC-08): a fake store isolates them from EF Core.</summary>
public class UpdateOrganizationSettingsHandlerTests
{
    [Fact]
    public async Task HandleAsync_SequencesNotAboveMax_ReturnsInvalidForEachFloor()
    {
        var organization = Organization.Create(
            "Acme", "Acme LLC", null, "ops@acme.com", "+1 555 123 4567",
            "America/Chicago", "USD", 7.25m, "Q", "WO", "INV", 1);
        var updatedAt = organization.UpdatedAt;

        var store = new FakeOrganizationSettingsStore(organization, maxInvoice: 500, maxQuote: 20, maxWorkOrder: 30);
        var handler = new UpdateOrganizationSettingsHandler(
            new UpdateOrganizationSettingsCommandValidator(), store, TimeProvider.System);

        var command = new UpdateOrganizationSettingsCommand(
            organization.Id,
            "Acme",
            "Acme LLC",
            null,
            "ops@acme.com",
            "+1 555 123 4567",
            "America/Chicago",
            "USD",
            7.25m,
            "Q",
            "WO",
            "INV",
            500,
            20,
            31,
            null,
            "1 Main St",
            "Austin",
            "TX",
            "78701",
            "US",
            JsonDocument.Parse("true").RootElement,
            false,
            updatedAt.ToString("O"),
            Guid.NewGuid(),
            null);

        var result = await handler.HandleAsync(command, CancellationToken.None);

        var invalid = Assert.IsType<UpdateOrganizationSettingsResult.Invalid>(result);
        Assert.Equal(2, invalid.Errors.Count);
        Assert.Equal(
            "Enter a number greater than the last invoice number.",
            Assert.Single(invalid.Errors["nextInvoiceNumber"]));
        Assert.Equal(
            "Enter a number greater than the last quote number.",
            Assert.Single(invalid.Errors["nextQuoteNumber"]));
        Assert.False(invalid.Errors.ContainsKey("nextWorkOrderNumber"));
        Assert.False(store.SaveCalled);
    }

    private sealed class FakeOrganizationSettingsStore(
        Organization organization, long maxInvoice, long maxQuote, long maxWorkOrder)
        : IOrganizationSettingsStore
    {
        public bool SaveCalled { get; private set; }

        public Task<Organization?> GetAsync(Guid organizationId, CancellationToken cancellationToken) =>
            Task.FromResult<Organization?>(organization);

        public Task<long> GetMaxInvoiceNumberAsync(Guid organizationId, CancellationToken cancellationToken) =>
            Task.FromResult(maxInvoice);

        public Task<long> GetMaxQuoteNumberAsync(Guid organizationId, CancellationToken cancellationToken) =>
            Task.FromResult(maxQuote);

        public Task<long> GetMaxWorkOrderNumberAsync(Guid organizationId, CancellationToken cancellationToken) =>
            Task.FromResult(maxWorkOrder);

        public Task<bool> HasInvoicesAsync(Guid organizationId, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<OrganizationLogoMetadata?> GetLogoMetadataAsync(
            Guid organizationId, CancellationToken cancellationToken) =>
            Task.FromResult<OrganizationLogoMetadata?>(null);

        public Task<bool> TrySaveUpdateAsync(
            Organization updated, AuditLog auditLog, CancellationToken cancellationToken)
        {
            SaveCalled = true;
            return Task.FromResult(true);
        }
    }
}
