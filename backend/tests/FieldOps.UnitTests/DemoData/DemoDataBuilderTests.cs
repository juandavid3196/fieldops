using FieldOps.Application.Authentication;
using FieldOps.DemoSeeder;
using FieldOps.Domain.Invoices;
using FieldOps.Domain.Notifications;
using FieldOps.Domain.Organizations;
using FieldOps.Domain.Quotes;
using FieldOps.Domain.Requests;
using FieldOps.Domain.Users;
using FieldOps.Domain.WorkOrders;

namespace FieldOps.UnitTests.DemoData;

public class DemoDataBuilderTests
{
    private static readonly string[] RoleCodes = ["owner", "operations_manager", "dispatcher", "accounting", "technician", "viewer"];

    [Fact]
    public void Build_ProducesAConsistentTenantThatCoversTheProductFlow()
    {
        var roles = RoleCodes.Select((code, index) => (code, id: (short)(index + 1))).ToDictionary(item => item.code, item => item.id);
        var set = new DemoDataBuilder(new StubHasher(), roles).Build("demo.example.com", "not-a-real-password", new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero));

        var users = set.Entities.OfType<User>().ToList();
        var requests = set.Entities.OfType<ServiceRequest>().ToList();
        var quotes = set.Entities.OfType<Quote>().ToList();
        var versions = set.Entities.OfType<QuoteVersion>().ToList();
        var workOrders = set.Entities.OfType<WorkOrder>().ToList();
        var visits = set.Entities.OfType<Visit>().ToList();
        var invoices = set.Entities.OfType<Invoice>().ToList();

        // Every role is reachable and credentials come only from the supplied password.
        Assert.All(RoleCodes, code => Assert.Contains(set.Entities.OfType<OrganizationUser>(), member => member.RoleId == roles[code]));
        Assert.Equal(users.Count, users.Select(user => user.Email).Distinct().Count());
        Assert.DoesNotContain(users, user => user.PasswordHash.Contains("not-a-real-password", StringComparison.Ordinal));

        // The funnel has every stage and the numbering counters moved past the used numbers.
        Assert.All(
            new[] { WorkOrderStatus.Scheduled, WorkOrderStatus.InProgress, WorkOrderStatus.Completed, WorkOrderStatus.ApprovedForBilling },
            status => Assert.Contains(workOrders, order => order.Status == status));
        Assert.Contains(requests, request => request.Status == RequestStatus.New);
        Assert.Contains(quotes, quote => quote.Status == QuoteStatus.Rejected);
        Assert.Contains(invoices, invoice => invoice.Status == InvoiceStatus.Paid);
        Assert.Contains(invoices, invoice => invoice.Status == InvoiceStatus.PartiallyPaid);
        Assert.Contains(invoices, invoice => invoice.Status == InvoiceStatus.Draft);
        Assert.Equal(requests.Count + 1, set.NextRequestNumber);
        Assert.Equal(workOrders.Max(order => order.WorkOrderNumber) + 1, set.NextWorkOrderNumber);
        Assert.Equal(invoices.Max(invoice => invoice.InvoiceNumber) + 1, set.NextInvoiceNumber);

        // References resolve and money adds up.
        Assert.All(workOrders, order => Assert.Contains(versions, version => version.Id == order.QuoteVersionId));
        Assert.All(visits, visit => Assert.Contains(workOrders, order => order.Id == visit.WorkOrderId));

        foreach (var invoice in invoices)
        {
            var lines = set.Entities.OfType<InvoiceLine>().Where(line => line.InvoiceId == invoice.Id).ToList();

            Assert.Equal(invoice.Total, lines.Sum(line => line.LineTotal));
            Assert.Equal(invoice.Total, invoice.AmountPaid + invoice.BalanceDue);
        }

        // Dashboards and the inbox have something to show, and nothing is addressed to an external recipient.
        Assert.NotEmpty(set.Entities.OfType<Notification>());
        Assert.All(set.Accounts, account => Assert.EndsWith("@demo.example.com", account.Email, StringComparison.Ordinal));
    }

    private sealed class StubHasher : IPasswordHasher
    {
        public string DummyHash => "stub";

        public string Hash(string password) => "stub-hash";

        public bool Verify(string password, string storedHash) => false;
    }
}
