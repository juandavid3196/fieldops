using FieldOps.Application.Features.Organizations;
using FieldOps.Domain.Notifications;
using FieldOps.Domain.Organizations;

namespace FieldOps.UnitTests.Organizations;

/// <summary>BR-02's floor check (AC-07): a fake store isolates it from EF Core.</summary>
public class UpdateOrganizationSettingsHandlerTests
{
    [Fact]
    public async Task HandleAsync_NextInvoiceNumberNotAboveMax_ReturnsInvalid()
    {
        var organization = Organization.Create(
            "Acme", "Acme LLC", null, "ops@acme.com", "+1 555 123 4567",
            "America/Chicago", "USD", 7.25m, "Q", "WO", "INV", 1);
        var updatedAt = organization.UpdatedAt;

        var store = new FakeOrganizationSettingsStore(organization, maxInvoiceNumber: 500);
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
            updatedAt.ToString("O"),
            Guid.NewGuid(),
            null);

        var result = await handler.HandleAsync(command, CancellationToken.None);

        var invalid = Assert.IsType<UpdateOrganizationSettingsResult.Invalid>(result);
        var error = Assert.Single(invalid.Errors);
        Assert.Equal("nextInvoiceNumber", error.Key);
        Assert.Equal("Enter a number greater than the last invoice number.", Assert.Single(error.Value));
        Assert.False(store.SaveCalled);
    }

    private sealed class FakeOrganizationSettingsStore(Organization organization, long maxInvoiceNumber)
        : IOrganizationSettingsStore
    {
        public bool SaveCalled { get; private set; }

        public Task<Organization?> GetAsync(Guid organizationId, CancellationToken cancellationToken) =>
            Task.FromResult<Organization?>(organization);

        public Task<long> GetMaxInvoiceNumberAsync(Guid organizationId, CancellationToken cancellationToken) =>
            Task.FromResult(maxInvoiceNumber);

        public Task<bool> TrySaveUpdateAsync(
            Organization updated, AuditLog auditLog, CancellationToken cancellationToken)
        {
            SaveCalled = true;
            return Task.FromResult(true);
        }
    }
}
