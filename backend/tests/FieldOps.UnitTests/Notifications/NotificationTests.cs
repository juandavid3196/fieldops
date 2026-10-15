using FieldOps.Domain.Notifications;

namespace FieldOps.UnitTests.Notifications;

public class NotificationTests
{
    [Fact]
    public void Create_WithValidArguments_SetsDefaults()
    {
        var organizationId = Guid.NewGuid();
        var recipientUserId = Guid.NewGuid();

        var notification = Notification.Create(
            organizationId,
            NotificationChannel.Email,
            " quote_sent ",
            recipientUserId: recipientUserId);

        Assert.NotEqual(Guid.Empty, notification.Id);
        Assert.Equal(organizationId, notification.OrganizationId);
        Assert.Equal(NotificationChannel.Email, notification.Channel);
        Assert.Equal("quote_sent", notification.TemplateCode);
        Assert.Equal(recipientUserId, notification.RecipientUserId);
        Assert.Null(notification.RecipientContactId);
        Assert.Equal("{}", notification.Payload);
        Assert.Equal(NotificationStatus.Pending, notification.Status);
        Assert.Null(notification.SentAt);
        Assert.Null(notification.FailureReason);
    }

    [Fact]
    public void Create_WithEmptyOrganizationId_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => Notification.Create(Guid.Empty, NotificationChannel.Sms, "template"));
    }

    [Fact]
    public void CreateInApp_ThenReadAndUnread_FollowsOnlyTheBr19Transitions()
    {
        var at = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
        var notification = Notification.CreateInApp(
            Guid.NewGuid(), Guid.NewGuid(), "quote.approved", " Quote Q-1 was approved ", "{\"type\":\"quote\"}", at);

        Assert.Equal(
            (NotificationChannel.InApp, NotificationStatus.Sent, "Quote Q-1 was approved", at, at, at),
            (notification.Channel, notification.Status, notification.Subject, notification.ScheduledAt, notification.SentAt, notification.CreatedAt));
        Assert.Null(notification.RecipientContactId);

        notification.MarkUnread();
        Assert.Equal(NotificationStatus.Sent, notification.Status);

        notification.MarkRead();
        notification.MarkRead();
        Assert.Equal(NotificationStatus.Read, notification.Status);

        notification.MarkUnread();
        notification.MarkUnread();
        Assert.Equal(NotificationStatus.Sent, notification.Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankTemplateCode_Throws(string templateCode)
    {
        Assert.Throws<ArgumentException>(
            () => Notification.Create(Guid.NewGuid(), NotificationChannel.InApp, templateCode));
    }
}
