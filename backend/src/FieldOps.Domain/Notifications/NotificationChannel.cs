namespace FieldOps.Domain.Notifications;

// notifications.channel is a varchar(20) with a CHECK constraint in the
// relational model, not a native PostgreSQL enum type.
public enum NotificationChannel
{
    Email,
    Sms,
    InApp,
}
