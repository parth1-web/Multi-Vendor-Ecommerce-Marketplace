using Marketplace.Domain.Auditing;
using Marketplace.Domain.Identity;
using Marketplace.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Marketplace.Infrastructure.Persistence.Configurations;

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");
        builder.HasKey(n => n.Id);

        builder.Property(n => n.Type).HasConversion<string>().HasMaxLength(48).IsRequired();
        builder.Property(n => n.Audience).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(n => n.Title).HasColumnType("varchar(200)").IsRequired();
        builder.Property(n => n.Body).HasColumnType("text").IsRequired();
        builder.Property(n => n.Link).HasColumnType("varchar(300)");

        builder.HasIndex(n => new { n.UserId, n.IsRead, n.CreatedAt }).HasDatabaseName("ix_notifications_user_unread");
        builder.HasIndex(n => new { n.Type, n.CreatedAt }).HasDatabaseName("ix_notifications_type");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(n => n.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.ActorEmail).HasColumnType("varchar(256)").IsRequired();
        builder.Property(a => a.Action).HasConversion<string>().HasMaxLength(48).IsRequired();
        builder.Property(a => a.EntityType).HasColumnType("varchar(64)").IsRequired();
        builder.Property(a => a.EntityName).HasColumnType("varchar(200)");
        builder.Property(a => a.ChangesJson).HasColumnType("jsonb");
        builder.Property(a => a.IpAddress).HasColumnType("varchar(64)");
        builder.Property(a => a.UserAgent).HasColumnType("varchar(400)");
        builder.Property(a => a.CorrelationId).HasColumnType("varchar(64)").IsRequired();

        builder.HasIndex(a => new { a.ActorId, a.CreatedAt }).HasDatabaseName("ix_audit_logs_actor");
        builder.HasIndex(a => new { a.EntityType, a.EntityId }).HasDatabaseName("ix_audit_logs_entity");
        builder.HasIndex(a => new { a.Action, a.CreatedAt }).HasDatabaseName("ix_audit_logs_action");
        builder.HasIndex(a => a.CorrelationId).HasDatabaseName("ix_audit_logs_correlation");
    }
}
