using FieldOps.Application.Features.PortalDashboard;
using FieldOps.Domain.Customers;
using FieldOps.Domain.Requests;
using FieldOps.Domain.WorkOrders;

namespace FieldOps.UnitTests.Portal;

/// <summary>Dashboard selection rules, labels and eligibility of the customer portal (BR-20 … BR-23, BR-26, BR-32, BR-38, BR-39).</summary>
public class PortalRulesTests
{
    private static readonly DateOnly Today = new(2026, 10, 13);

    private static readonly DateTimeOffset Now = new(2026, 10, 13, 15, 0, 0, TimeSpan.Zero);

    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-00000000000a");

    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-00000000000b");

    private static readonly Guid C = Guid.Parse("00000000-0000-0000-0000-00000000000c");

    [Fact]
    public void PickActionQuote_AppliesStatusExpiryAndOrderingRules()
    {
        var sent = Now.AddDays(-2);

        (Guid? Pick, int More) Run(params QuoteCandidate[] candidates)
        {
            var (pick, more) = PortalSelectionRules.PickActionQuote(candidates, Today);

            return (pick?.Id, more);
        }

        // Earliest valid-until wins, a missing one sorts last, the newest sent time breaks a tie.
        Assert.Equal((B, 2), Run(new(A, "sent", Today.AddDays(9), sent), new(B, "sent", Today.AddDays(2), sent), new(C, "clarification_requested", null, sent)));
        Assert.Equal((A, 1), Run(new(A, "sent", null, sent.AddHours(1)), new(B, "sent", null, sent)));
        Assert.Equal((B, 1), Run(new(A, "sent", Today, sent), new(B, "sent", Today, sent.AddHours(1))));

        // The valid-until day itself still counts; a past day, another status or no candidate is the empty card.
        Assert.Equal((A, 0), Run(new QuoteCandidate(A, "sent", Today, sent)));
        Assert.Equal((null, 0), Run(new(A, "sent", Today.AddDays(-1), sent), new(B, "approved", null, sent), new(C, "draft", null, sent)));
        Assert.Equal((null, 0), Run());
    }

    [Fact]
    public void PickPaymentDueAndOverdue_AppliesStatusBalanceAndOrderingRules()
    {
        (Guid? Pick, int More) Run(params InvoiceCandidate[] candidates)
        {
            var (pick, more) = PortalSelectionRules.PickPaymentDue(candidates);

            return (pick?.Id, more);
        }

        Assert.Equal((B, 2), Run(new(A, "sent", 10m, Today.AddDays(5), 1), new(B, "overdue", 20m, Today.AddDays(-3), 3), new(C, "partially_paid", 5m, null, 2)));
        Assert.Equal((B, 1), Run(new(A, "sent", 10m, null, 2), new(B, "sent", 10m, null, 1)));
        Assert.Equal((null, 0), Run(new(A, "paid", 0m, Today, 1), new(B, "sent", 0m, Today, 2), new(C, "draft", 10m, Today, 3), new(A, "void", 10m, Today, 4)));

        Assert.True(PortalSelectionRules.IsOverdue("overdue", null, Today));
        Assert.True(PortalSelectionRules.IsOverdue("sent", Today.AddDays(-1), Today));
        Assert.False(PortalSelectionRules.IsOverdue("sent", Today, Today));
        Assert.False(PortalSelectionRules.IsOverdue("sent", null, Today));
    }

    [Fact]
    public void PickUpcomingAndActivityRows_FollowVisitProgressAndInvoiceState()
    {
        var startOfToday = new DateTimeOffset(2026, 10, 13, 0, 0, 0, TimeSpan.Zero);

        Guid? Upcoming(params VisitCandidate[] candidates) => PortalSelectionRules.PickUpcoming(candidates, startOfToday)?.VisitId;

        // A visit in progress first, then the earliest start from the start of today; yesterday and unscheduled ones never.
        Assert.Equal(C, Upcoming(new(A, VisitStatus.Scheduled, Now.AddDays(1)), new(B, VisitStatus.Assigned, Now.AddHours(2)), new(C, VisitStatus.InProgress, Now.AddDays(3))));
        Assert.Equal(B, Upcoming(new(A, VisitStatus.Scheduled, Now.AddDays(1)), new(B, VisitStatus.Assigned, startOfToday.AddHours(1)), new(C, VisitStatus.Scheduled, startOfToday.AddHours(-1))));
        Assert.Null(Upcoming(new(A, VisitStatus.Scheduled, startOfToday.AddHours(-1)), new(B, VisitStatus.Completed, Now), new(C, VisitStatus.Scheduled, null)));

        var facts = new ActivityFacts(A, "Fix drain", Today, "WO-7", Guid.NewGuid(), null, null, null, null, null);
        var receipt = Guid.NewGuid();

        Assert.Equal(("paid", "WO-7", (decimal?)80m, (Guid?)receipt), Row(facts with { InvoiceStatus = "paid", InvoiceNumber = "INV-9", InvoiceTotal = 80m, InvoiceCurrency = "USD", ReceiptPaymentId = receipt }));
        Assert.Equal(("invoice_due", "INV-9", (decimal?)80m, (Guid?)null), Row(facts with { InvoiceStatus = "overdue", InvoiceNumber = "INV-9", InvoiceTotal = 80m, InvoiceCurrency = "USD", ReceiptPaymentId = receipt }));
        Assert.Equal(("completed", "WO-7", (decimal?)null, (Guid?)null), Row(facts with { InvoiceStatus = null }));

        static (string Status, string Reference, decimal? Amount, Guid? Receipt) Row(ActivityFacts facts)
        {
            var row = PortalSelectionRules.ToActivityRow(facts, "USD");

            return (row.Status, row.Reference, row.Amount, row.ReceiptPaymentId);
        }
    }

    [Fact]
    public void StepperLabelsAndRescheduleEligibility_FollowTheCustomerFacingRules()
    {
        // BR-38: every visit status maps to one stepper position.
        Assert.Equal(
            ["scheduled", "scheduled", "on_the_way", "in_progress", "in_progress", "completed", "completed", "completed"],
            new[]
            {
                VisitStatus.Scheduled, VisitStatus.Assigned, VisitStatus.OnTheWay, VisitStatus.InProgress,
                VisitStatus.Paused, VisitStatus.Completed, VisitStatus.NeedsCorrection, VisitStatus.Approved,
            }.Select(PortalVisitRules.ProgressStep).ToArray());

        Assert.Equal(
            ["Submitted", "Under review", "Assessment scheduled", "Preparing quote", "Quote ready", "Job created", "Cancelled"],
            new[]
            {
                RequestStatus.New, RequestStatus.NeedsReview, RequestStatus.AssessmentScheduled, RequestStatus.ReadyForQuote,
                RequestStatus.Quoted, RequestStatus.Converted, RequestStatus.Cancelled,
            }.Select(PortalRequestRules.StatusLabel).ToArray());

        // Only a scheduled or assigned visit in the future without a pending request can be rescheduled.
        var future = Now.AddDays(1);

        Assert.True(PortalVisitRules.CanRequestReschedule(VisitStatus.Scheduled, future, Now, false));
        Assert.True(PortalVisitRules.CanRequestReschedule(VisitStatus.Assigned, future, Now, false));
        Assert.False(PortalVisitRules.CanRequestReschedule(VisitStatus.OnTheWay, future, Now, false));
        Assert.False(PortalVisitRules.CanRequestReschedule(VisitStatus.Scheduled, Now.AddMinutes(-1), Now, false));
        Assert.False(PortalVisitRules.CanRequestReschedule(VisitStatus.Scheduled, null, Now, false));
        Assert.False(PortalVisitRules.CanRequestReschedule(VisitStatus.Scheduled, future, Now, true));

        // A request is pending only while the visit still starts when the request was made.
        Assert.True(PortalVisitRules.IsPending(future, future));
        Assert.False(PortalVisitRules.IsPending(future, future.AddHours(1)));
        Assert.False(PortalVisitRules.IsPending(future, null));
    }

    [Fact]
    public void PortalLinkAndInvitation_KeepTheLinkTimeLastNameAndOpenStateConsistent()
    {
        var contact = CustomerContact.Create(Guid.NewGuid(), Guid.NewGuid(), "Pat");
        var user = Guid.NewGuid();

        contact.SetLastNameIfMissing("  Smith ", Now);
        contact.SetLastNameIfMissing("Jones", Now.AddMinutes(1));
        Assert.Equal("Smith", contact.LastName);

        contact.LinkPortal(user, Now);
        Assert.Equal((user, Now), (contact.PortalUserId, contact.PortalLinkedAt));
        Assert.Throws<ArgumentException>(() => contact.LinkPortal(Guid.Empty, Now));
        Assert.True(contact.UnlinkPortal(Now.AddHours(1)));
        Assert.Null(contact.PortalUserId);
        Assert.Null(contact.PortalLinkedAt);
        Assert.False(contact.UnlinkPortal(Now.AddHours(2)));

        var invitation = CustomerPortalInvitation.Create(contact.OrganizationId, contact.Id, " Pat@Example.com ", "hash", Guid.NewGuid(), Now);
        Assert.Equal("pat@example.com", invitation.Email);
        Assert.Equal(Now.AddDays(7), invitation.ExpiresAt);
        Assert.True(invitation.IsOpen(Now.AddDays(7).AddTicks(-1)));
        Assert.False(invitation.IsOpen(Now.AddDays(7)));

        invitation.Revoke(Now.AddDays(1));
        Assert.False(invitation.IsOpen(Now.AddDays(2)));
        Assert.Equal(Now.AddDays(1), invitation.RevokedAt);
    }
}
