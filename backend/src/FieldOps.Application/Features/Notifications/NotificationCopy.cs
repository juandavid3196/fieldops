using System.Globalization;

namespace FieldOps.Application.Features.Notifications;

/// <summary>Snapshot content of one notification: copy of BR-07 and labels of BR-08.</summary>
public sealed record NotificationContent(
    string EntityKind, string EntityLabel, string? SecondaryLabel, string Title, string Description);

/// <summary>Pure builders of the stored title, description and labels (notifications-center BR-07, BR-08).</summary>
public static class NotificationCopy
{
    public const int SubjectMaxLength = 240;

    public static string Money(decimal amount, string currency) =>
        string.Create(CultureInfo.InvariantCulture, $"{amount:N2} {currency}");

    public static NotificationContent RequestCreated(string requestNumber, string name) =>
        new(
            NotificationEntityKinds.Request,
            requestNumber,
            null,
            "New service request",
            $"{name} submitted {requestNumber}.");

    public static NotificationContent CustomerMessage(string customerName, string contactName) =>
        new(
            NotificationEntityKinds.Customer,
            customerName,
            null,
            "Customer sent a message",
            $"{contactName} from {customerName} sent a message from the customer portal.");

    public static NotificationContent QuoteApproved(string quoteNumber, string? requestNumber, string customer, string approvedTotal) =>
        new(
            NotificationEntityKinds.Quote,
            quoteNumber,
            requestNumber,
            $"Quote {quoteNumber} was approved",
            $"{customer} approved the quote for {approvedTotal}.");

    public static NotificationContent QuoteRejected(string quoteNumber, string? requestNumber, string customer) =>
        new(
            NotificationEntityKinds.Quote,
            quoteNumber,
            requestNumber,
            $"Quote {quoteNumber} was declined",
            $"{customer} declined the quote.");

    public static NotificationContent QuoteClarification(string quoteNumber, string? requestNumber, string customer) =>
        new(
            NotificationEntityKinds.Quote,
            quoteNumber,
            requestNumber,
            $"Question about quote {quoteNumber}",
            $"{customer} requested a clarification.");

    public static NotificationContent VisitAssigned(
        string orderNumber, int visitNumber, string technician, DateTimeOffset scheduledStart, TimeZoneInfo zone) =>
        Visit(
            orderNumber,
            visitNumber,
            "Visit assigned",
            $"{orderNumber} was assigned to {technician} for "
                + $"{TimeZoneInfo.ConvertTime(scheduledStart, zone).ToString("MMM d, h:mm tt", CultureInfo.InvariantCulture)}.");

    public static NotificationContent VisitOnTheWay(string orderNumber, int visitNumber, string technician) =>
        Visit(orderNumber, visitNumber, "Technician on the way", $"{technician} is on the way to {orderNumber}.");

    public static NotificationContent VisitStarted(string orderNumber, int visitNumber, string technician) =>
        Visit(orderNumber, visitNumber, "Visit started", $"{technician} started {orderNumber}.");

    public static NotificationContent VisitPaused(string orderNumber, int visitNumber, string technician) =>
        Visit(orderNumber, visitNumber, "Visit paused", $"{technician} paused {orderNumber}.");

    public static NotificationContent JobCompleted(string orderNumber, string technician) =>
        new(
            NotificationEntityKinds.WorkOrder,
            orderNumber,
            null,
            "Job completed",
            $"{technician} completed {orderNumber}.");

    public static NotificationContent InvoiceSent(string invoiceNumber, string customer, string total) =>
        new(
            NotificationEntityKinds.Invoice,
            invoiceNumber,
            null,
            $"Invoice {invoiceNumber} was sent",
            $"{invoiceNumber} was sent to {customer} for {total}.");

    public static NotificationContent BankTransferReported(string invoiceNumber, string customer, string amount) =>
        new(
            NotificationEntityKinds.Invoice,
            invoiceNumber,
            null,
            "Bank transfer reported",
            $"{customer} reported a bank transfer of {amount} for {invoiceNumber}.");

    public static NotificationContent PaymentReceived(string invoiceNumber, string amount) =>
        new(
            NotificationEntityKinds.Invoice,
            invoiceNumber,
            null,
            "Payment received",
            $"Payment of {amount} was recorded for invoice {invoiceNumber}.");

    /// <summary>The title column is varchar(240); a pathological name is cut, never rejected.</summary>
    public static string Truncate(string title) =>
        title.Length <= SubjectMaxLength ? title : title[..SubjectMaxLength].TrimEnd();

    private static NotificationContent Visit(string orderNumber, int visitNumber, string title, string description) =>
        new(
            NotificationEntityKinds.Visit,
            orderNumber,
            string.Create(CultureInfo.InvariantCulture, $"Visit {visitNumber}"),
            title,
            description);
}
