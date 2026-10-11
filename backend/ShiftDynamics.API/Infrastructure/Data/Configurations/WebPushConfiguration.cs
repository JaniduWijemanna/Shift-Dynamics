using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShiftDynamics.API.Domain.Entities;

namespace ShiftDynamics.API.Infrastructure.Data.Configurations;

public class WebPushSubscriptionConfiguration : IEntityTypeConfiguration<WebPushSubscription>
{
    public void Configure(EntityTypeBuilder<WebPushSubscription> b)
    {
        b.ToTable("web_push_subscriptions", t => t.ExcludeFromMigrations());
        b.HasKey(x => x.Id);
        b.Property(x => x.Token).HasColumnType("nvarchar(max)").HasMaxLength(4096).IsRequired();
        b.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.Origin).HasMaxLength(500).IsRequired();
        b.Property(x => x.BasePath).HasMaxLength(300).IsRequired();
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => new { x.UserId, x.Enabled });
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
    }
}

public class WebPushDeliveryConfiguration : IEntityTypeConfiguration<WebPushDelivery>
{
    public void Configure(EntityTypeBuilder<WebPushDelivery> b)
    {
        b.ToTable("web_push_deliveries", t => t.ExcludeFromMigrations());
        b.HasKey(x => x.Id);
        b.Property(x => x.LeaseKey).HasMaxLength(36);
        b.Property(x => x.LastError).HasMaxLength(200);
        b.HasIndex(x => new { x.NotificationId, x.SubscriptionId }).IsUnique();
        b.HasIndex(x => new { x.Status, x.NextAttemptAt });
        b.HasOne(x => x.Notification).WithMany().HasForeignKey(x => x.NotificationId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.Subscription).WithMany().HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.NoAction);
    }
}