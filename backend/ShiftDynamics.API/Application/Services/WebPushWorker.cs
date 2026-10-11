using FirebaseAdmin.Messaging;
using Microsoft.EntityFrameworkCore;
using ShiftDynamics.API.Domain.Entities;
using ShiftDynamics.API.Infrastructure.Data;

namespace ShiftDynamics.API.Application.Services;

public sealed class WebPushWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly FirebaseWebPushSender _sender;
    private readonly ILogger<WebPushWorker> _logger;
    private DateTime _lastWarning = DateTime.MinValue;
    public WebPushWorker(IServiceScopeFactory scopes, FirebaseWebPushSender sender, ILogger<WebPushWorker> logger)
    { _scopes = scopes; _sender = sender; _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_sender.Configured) await DispatchAsync(stoppingToken);
                else Warn("Backend Firebase credentials are not configured; web push is paused.");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception e) { Warn($"Web push worker could not process notifications ({e.GetType().Name}). Check Firebase credentials and run the web-push setup command if tables are missing."); }
            try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
    private void Warn(string message)
    {
        if (_lastWarning > DateTime.UtcNow.AddMinutes(-1)) return;
        _lastWarning = DateTime.UtcNow;
        _logger.LogWarning("{Message}", message);
    }

    private async Task DispatchAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShiftDynamicsDbContext>();
        // Queue only new, unread notifications created after a browser opted in.
        // The transaction lock and unique index prevent competing workers inserting the same delivery.
        await db.Database.ExecuteSqlRawAsync("""
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;
            DECLARE @lockResult int;
            EXEC @lockResult = sp_getapplock @Resource = N'ShyftDynamics.WebPush.Queue',
                @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 0;
            IF @lockResult >= 0
            BEGIN
                INSERT INTO dbo.web_push_deliveries
                    (Id, NotificationId, SubscriptionId, Status, Attempts, NextAttemptAt)
                SELECT TOP (100) NEWID(), n.Id, s.Id, 0, 0, SYSUTCDATETIME()
                FROM dbo.notifications n
                JOIN dbo.web_push_subscriptions s ON s.UserId = n.UserId AND s.Enabled = 1
                JOIN dbo.users u ON u.Id = n.UserId AND u.Status = 0
                WHERE n.IsRead = 0 AND n.CreatedAt >= s.ActivatedAt
                    AND n.CreatedAt >= DATEADD(day, -1, SYSUTCDATETIME())
                    AND s.UpdatedAt >= DATEADD(day, -90, SYSUTCDATETIME())
                    AND NOT EXISTS (SELECT 1 FROM dbo.web_push_deliveries d
                        WHERE d.NotificationId = n.Id AND d.SubscriptionId = s.Id)
                ORDER BY n.CreatedAt;
            END;
            COMMIT TRANSACTION;
            """, ct);
        var lease = Guid.NewGuid().ToString();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            ;WITH ready AS (
                SELECT TOP (10) * FROM dbo.web_push_deliveries WITH (UPDLOCK, ROWLOCK)
                WHERE (Status = 0 OR (Status = 1 AND LeaseUntil < SYSUTCDATETIME()))
                    AND NextAttemptAt <= SYSUTCDATETIME()
                ORDER BY NextAttemptAt
            )
            UPDATE ready SET Status = 1, LeaseKey = {lease},
                LeaseUntil = DATEADD(minute, 5, SYSUTCDATETIME()), Attempts = Attempts + 1;
            """, ct);
        var deliveries = await db.WebPushDeliveries.Where(d => d.LeaseKey == lease && d.Status == WebPushDeliveryStatus.Processing)
            .Include(d => d.Notification).Include(d => d.Subscription).ThenInclude(s => s.User).ToListAsync(ct);
        foreach (var delivery in deliveries)
        {
            var subscription = delivery.Subscription;
            // Recheck opt-out and account state immediately before delivery, without stale tracked values.
            await db.Entry(subscription).ReloadAsync(ct);
            await db.Entry(subscription.User).ReloadAsync(ct);
            await db.Entry(delivery.Notification).ReloadAsync(ct);
            if (!subscription.Enabled || subscription.User.Status != AccountStatus.Active ||
                subscription.UserId != delivery.Notification.UserId || delivery.Notification.IsRead ||
                delivery.Notification.CreatedAt < subscription.ActivatedAt)
                delivery.Status = WebPushDeliveryStatus.Skipped;
            else
            {
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(TimeSpan.FromSeconds(20));
                    await _sender.SendAsync(subscription, delivery.Notification, subscription.User.Role, timeout.Token);
                    delivery.Status = WebPushDeliveryStatus.Sent;
                    delivery.SentAt = DateTime.UtcNow;
                    delivery.LastError = null;
                    _logger.LogInformation("Firebase accepted web push for notification {NotificationId}.", delivery.NotificationId);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (FirebaseMessagingException e) when (e.MessagingErrorCode is MessagingErrorCode.Unregistered or MessagingErrorCode.SenderIdMismatch)
                {
                    subscription.Enabled = false;
                    delivery.Status = WebPushDeliveryStatus.Failed;
                    delivery.LastError = e.MessagingErrorCode.ToString();
                    Warn("An expired or mismatched browser push token was disabled. Enable push again in that browser.");
                }
                catch (Exception e)
                {
                    // Store only safe error types/codes; never credentials, raw registration tokens or payloads.
                    delivery.LastError = e is FirebaseMessagingException f ? f.MessagingErrorCode.ToString() : e.GetType().Name;
                    delivery.Status = delivery.Attempts >= 5 ? WebPushDeliveryStatus.Failed : WebPushDeliveryStatus.Pending;
                    delivery.NextAttemptAt = DateTime.UtcNow.AddSeconds(Math.Min(300, 15 * Math.Pow(2, delivery.Attempts)));
                    Warn($"Web push attempt failed ({delivery.LastError}). Pending messages will be retried.");
                }
            }
            delivery.LeaseKey = null; delivery.LeaseUntil = null;
            await db.SaveChangesAsync(ct);
        }
    }
}