namespace ShiftDynamics.API.Domain.Entities;

public class WebPushSubscription
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Token { get; set; } = "";
    public string TokenHash { get; set; } = "";
    public string Origin { get; set; } = "";
    public string BasePath { get; set; } = "/frontend/";
    public bool Enabled { get; set; }
    public DateTime ActivatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public User User { get; set; } = null!;
}

public enum WebPushDeliveryStatus { Pending, Processing, Sent, Failed, Skipped }

public class WebPushDelivery
{
    public Guid Id { get; set; }
    public Guid NotificationId { get; set; }
    public Guid SubscriptionId { get; set; }
    public WebPushDeliveryStatus Status { get; set; }
    public int Attempts { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public string? LeaseKey { get; set; }
    public DateTime? LeaseUntil { get; set; }
    public DateTime? SentAt { get; set; }
    public string? LastError { get; set; }
    public Notification Notification { get; set; } = null!;
    public WebPushSubscription Subscription { get; set; } = null!;
}