using Microsoft.EntityFrameworkCore;
using ShiftDynamics.API.Infrastructure.Data;

namespace ShiftDynamics.API.Application.Services;

public static class WebPushSchema
{
    // Explicit, additive setup command; never drop or alter existing business tables.
    // These optional tables are excluded from EF migrations and managed by this initializer.
    public static async Task InitializeAsync(ShiftDynamicsDbContext db, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync("""
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;
            IF OBJECT_ID(N'dbo.notifications', N'U') IS NULL OR OBJECT_ID(N'dbo.users', N'U') IS NULL
                THROW 51000, 'Existing users and notifications tables are required.', 1;
            IF OBJECT_ID(N'dbo.web_push_subscriptions', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.web_push_subscriptions (
                    Id uniqueidentifier NOT NULL PRIMARY KEY,
                    UserId uniqueidentifier NOT NULL,
                    Token nvarchar(max) NOT NULL,
                    TokenHash nvarchar(64) NOT NULL,
                    Origin nvarchar(500) NOT NULL,
                    BasePath nvarchar(300) NOT NULL,
                    Enabled bit NOT NULL,
                    ActivatedAt datetime2 NOT NULL,
                    UpdatedAt datetime2 NOT NULL,
                    CONSTRAINT FK_web_push_subscriptions_users FOREIGN KEY (UserId) REFERENCES dbo.users(Id)
                );
                CREATE UNIQUE INDEX IX_web_push_subscriptions_TokenHash ON dbo.web_push_subscriptions(TokenHash);
                CREATE INDEX IX_web_push_subscriptions_UserId_Enabled ON dbo.web_push_subscriptions(UserId, Enabled);
            END;
            IF OBJECT_ID(N'dbo.web_push_deliveries', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.web_push_deliveries (
                    Id uniqueidentifier NOT NULL PRIMARY KEY,
                    NotificationId uniqueidentifier NOT NULL,
                    SubscriptionId uniqueidentifier NOT NULL,
                    Status int NOT NULL,
                    Attempts int NOT NULL,
                    NextAttemptAt datetime2 NOT NULL,
                    LeaseKey nvarchar(36) NULL,
                    LeaseUntil datetime2 NULL,
                    SentAt datetime2 NULL,
                    LastError nvarchar(200) NULL,
                    CONSTRAINT FK_web_push_deliveries_notifications FOREIGN KEY (NotificationId) REFERENCES dbo.notifications(Id),
                    CONSTRAINT FK_web_push_deliveries_subscriptions FOREIGN KEY (SubscriptionId) REFERENCES dbo.web_push_subscriptions(Id)
                );
                CREATE UNIQUE INDEX IX_web_push_deliveries_NotificationId_SubscriptionId ON dbo.web_push_deliveries(NotificationId, SubscriptionId);
                CREATE INDEX IX_web_push_deliveries_Status_NextAttemptAt ON dbo.web_push_deliveries(Status, NextAttemptAt);
            END;
            COMMIT TRANSACTION;
            """, ct);
    }
}