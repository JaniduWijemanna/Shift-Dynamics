using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using ShiftDynamics.API.Domain.Entities;

namespace ShiftDynamics.API.Application.Services;

public sealed class FirebaseWebPushSender : IDisposable
{
    private readonly IConfiguration _configuration;
    private readonly object _gate = new();
    private FirebaseApp? _app;
    public FirebaseWebPushSender(IConfiguration configuration) => _configuration = configuration;
    public bool Configured => File.Exists(_configuration["Firebase:CredentialsPath"] ??
        Environment.GetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS"));

    private FirebaseMessaging Messaging()
    {
        lock (_gate)
        {
            if (_app is null)
            {
                var path = _configuration["Firebase:CredentialsPath"];
                var credential = string.IsNullOrWhiteSpace(path)
                    ? GoogleCredential.GetApplicationDefault()
                    : GoogleCredential.FromFile(path);
                _app = FirebaseApp.Create(new AppOptions {
                    Credential = credential, ProjectId = "shyftdynamics"
                }, "shyftdynamics-web-push");
            }
            return FirebaseMessaging.GetMessaging(_app);
        }
    }

    public async Task SendAsync(WebPushSubscription subscription,
        ShiftDynamics.API.Domain.Entities.Notification notification, SystemRole role, CancellationToken ct)
    {
        var folder = role switch {
            SystemRole.Customer => "customer", SystemRole.ServiceAdvisor => "advisor",
            SystemRole.Mechanic => "mechanic", SystemRole.Storekeeper => "storekeeper",
            SystemRole.Vendor => "vendor", _ => "manager"
        };
        string module = (role, notification.EntityType) switch {
            (SystemRole.Customer, "Appointment") => "booking",
            (SystemRole.Customer, "Estimate") => "estimates",
            (SystemRole.Customer, "Invoice") => "payments",
            (SystemRole.Customer, "ModificationRequest") => "modifications",
            (SystemRole.Customer, "EmergencyRequest") => "emergency",
            (SystemRole.Customer, "WorkOrder") => notification.Type == "handover" ? "history" : "tracker",
            (SystemRole.ServiceAdvisor, "Appointment") => "bookings",
            (SystemRole.ServiceAdvisor, "Estimate") => "estimates",
            (SystemRole.ServiceAdvisor, "Invoice") => "invoices",
            (SystemRole.ServiceAdvisor, "ModificationRequest") => "modifications",
            (SystemRole.ServiceAdvisor, "EmergencyRequest") => "emergency",
            (SystemRole.ServiceAdvisor, "WorkOrder") => "jobs",
            (SystemRole.Mechanic, "WorkOrder") => "assigned",
            (SystemRole.Mechanic, "PartRequisition") => "parts",
            (SystemRole.Storekeeper, "PartRequisition") => "requisitions",
            (SystemRole.Storekeeper, "PurchaseOrder") => "receiving",
            (SystemRole.Storekeeper, "QuoteRequest" or "VendorQuote") => "requests",
            (SystemRole.Vendor, "QuoteRequest") => "requests",
            (SystemRole.Vendor, "PurchaseOrder") => "deliveries",
            (SystemRole.Manager or SystemRole.Admin, "WorkOrder") => "workshop",
            (SystemRole.Manager or SystemRole.Admin, "VendorQuote") => "quotes",
            (SystemRole.Manager or SystemRole.Admin, "PurchaseOrder") => "orders",
            _ => "overview"
        };
        var url = $"{subscription.Origin}{subscription.BasePath}{folder}/dashboard.html?pushNotification={notification.Id}#{module}";
        // Data-only messages: the service worker displays once and checks the active browser account.
        // Detailed customer/job information stays behind the authenticated portal.
        var message = new Message {
            Token = subscription.Token,
            Data = new Dictionary<string, string> {
                ["userId"] = subscription.UserId.ToString(),
                ["notificationId"] = notification.Id.ToString(),
                ["title"] = notification.Title,
                ["body"] = "You have a new ShyftDynamics update. Open your portal to view the details.",
                ["url"] = url
            },
            Webpush = new WebpushConfig {
                Headers = new Dictionary<string, string> { ["TTL"] = "3600", ["Urgency"] = "normal" }
            }
        };
        await Messaging().SendAsync(message, ct);
    }
    public void Dispose() { lock (_gate) { _app?.Delete(); _app = null; } }
}