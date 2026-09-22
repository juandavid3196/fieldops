using FieldOps.Domain.Customers;
using FieldOps.Domain.Notifications;
using FieldOps.Domain.Organizations;
using FieldOps.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications", table => table.HasCheckConstraint(
            "ck_notifications_channel",
            "channel IN ('email','sms','in_app')"));

        builder.HasKey(notification => notification.Id);

        builder.Property(notification => notification.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(notification => notification.OrganizationId)
            .IsRequired();

        builder.Property(notification => notification.RecipientUserId);

        builder.Property(notification => notification.RecipientContactId);

        // varchar(20) with CHECK, not a native PostgreSQL enum type. Mapped
        // explicitly (not via generic lowercasing) because 'in_app' is not
        // the naive lowercase of InApp.
        builder.Property(notification => notification.Channel)
            .HasConversion(
                channel => ToChannelColumnValue(channel),
                value => ToNotificationChannel(value))
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(notification => notification.TemplateCode)
            .HasMaxLength(80)
            .IsRequired();

        builder.Property(notification => notification.Subject)
            .HasMaxLength(240);

        builder.Property(notification => notification.Payload)
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb")
            .IsRequired();

        // PostgreSQL enum notification_status, mapped in FieldOpsDbContext.
        builder.Property(notification => notification.Status)
            .IsRequired();

        builder.Property(notification => notification.ScheduledAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(notification => notification.SentAt);

        builder.Property(notification => notification.FailureReason)
            .HasColumnType("text");

        builder.Property(notification => notification.CreatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(notification => notification.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(notification => notification.RecipientUserId)
            .OnDelete(DeleteBehavior.NoAction);

        // recipient_contact_id REFERENCES customer_contacts (id):
        // single-column, not tenant-composite, exactly as defined in the
        // relational model.
        builder.HasOne<CustomerContact>()
            .WithMany()
            .HasForeignKey(notification => notification.RecipientContactId)
            .OnDelete(DeleteBehavior.NoAction);
    }

    private static string ToChannelColumnValue(NotificationChannel channel) => channel switch
    {
        NotificationChannel.Email => "email",
        NotificationChannel.Sms => "sms",
        NotificationChannel.InApp => "in_app",
        _ => throw new ArgumentOutOfRangeException(
            nameof(channel),
            channel,
            "Unknown notification channel."),
    };

    private static NotificationChannel ToNotificationChannel(string value) => value switch
    {
        "email" => NotificationChannel.Email,
        "sms" => NotificationChannel.Sms,
        "in_app" => NotificationChannel.InApp,
        _ => throw new ArgumentOutOfRangeException(
            nameof(value),
            value,
            "Unknown notification channel."),
    };
}
