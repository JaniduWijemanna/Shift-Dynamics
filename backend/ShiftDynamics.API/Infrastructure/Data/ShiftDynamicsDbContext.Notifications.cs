using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using ShiftDynamics.API.Domain.Entities;
using System.Text.RegularExpressions;

namespace ShiftDynamics.API.Infrastructure.Data;

public partial class ShiftDynamicsDbContext
{
    // Add event notifications to the same database save as the business change.
    // Existing assignment, sent-estimate, issued-invoice and payment alerts remain in their services.
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        PreparePortalNotificationsAsync(CancellationToken.None).GetAwaiter().GetResult();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        await PreparePortalNotificationsAsync(cancellationToken).ConfigureAwait(false);
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken).ConfigureAwait(false);
    }

    private async Task PreparePortalNotificationsAsync(CancellationToken ct)
    {
        ChangeTracker.DetectChanges();
        var changes = ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified)
            .Where(e => e.Entity is Appointment or WorkOrder or ModificationRequest or
                PartRequisition or Estimate or EmergencyRequest or QuoteRequest or VendorQuote or PurchaseOrder)
            .ToArray();
        if (changes.Length == 0) return;

        var users = await Users.AsNoTracking().Where(u => u.Status == AccountStatus.Active)
            .ToListAsync(ct).ConfigureAwait(false);
        var pending = ChangeTracker.Entries<Notification>()
            .Where(e => e.State == EntityState.Added).Select(e => e.Entity).ToList();
        void Notify(IEnumerable<Guid> targets, string type, string title, string body, string entityType, Guid entityId)
        {
            foreach (var userId in targets.Distinct())
            {
                if (!users.Any(u => u.Id == userId)) continue;
                if (pending.Any(n => n.UserId == userId && n.Type == type && n.EntityId == entityId && n.Body == body)) continue;
                var n = new Notification {
                    Id = Guid.NewGuid(), UserId = userId, Type = type, Title = title,
                    Body = body, EntityType = entityType, EntityId = entityId,
                    IsRead = false, CreatedAt = DateTime.UtcNow
                };
                Notifications.Add(n);
                pending.Add(n);
            }
        }
        IEnumerable<Guid> Roles(params SystemRole[] roles) => users.Where(u => roles.Contains(u.Role)).Select(u => u.Id);
        IEnumerable<Guid> CustomerUsers(Guid id) => users.Where(u => u.Role == SystemRole.Customer && u.CustomerId == id).Select(u => u.Id);
        bool Changed(EntityEntry e, string property) => e.State == EntityState.Modified &&
            !Equals(e.Property(property).OriginalValue, e.Property(property).CurrentValue);

        var requisitions = changes.Select(e => e.Entity).OfType<PartRequisition>().ToArray();
        var staffIds = requisitions.Select(r => r.RequestedByStaffId).Distinct().ToArray();
        var staff = staffIds.Length == 0 ? new List<Staff>() : await Staff.AsNoTracking()
            .Where(s => staffIds.Contains(s.Id)).ToListAsync(ct).ConfigureAwait(false);
        var orderQuoteIds = changes.Select(e => e.Entity).OfType<PurchaseOrder>().Select(o => o.VendorQuoteId).Distinct().ToArray();
        var vendorQuotes = orderQuoteIds.Length == 0 ? new List<VendorQuote>() : await VendorQuotes.AsNoTracking()
            .Where(q => orderQuoteIds.Contains(q.Id)).ToListAsync(ct).ConfigureAwait(false);
        vendorQuotes = vendorQuotes.Concat(VendorQuotes.Local).GroupBy(q => q.Id).Select(g => g.Last()).ToList();
        var needsVendors = changes.Any(e => e.Entity is QuoteRequest or PurchaseOrder);
        var vendors = !needsVendors ? new List<VendorProfile>() : await VendorProfiles.AsNoTracking()
            .Where(v => v.ApprovalStatus == VendorApprovalStatus.Active).ToListAsync(ct).ConfigureAwait(false);

        foreach (var entry in changes)
        {
            bool added = entry.State == EntityState.Added;
            switch (entry.Entity)
            {
                case Appointment a:
                    if (added)
                        Notify(Roles(SystemRole.ServiceAdvisor, SystemRole.Admin), "booking", "New service booking",
                            "A customer requested a service booking. Open bookings to review the date, vehicle and service.", "Appointment", a.Id);
                    else if (Changed(entry, nameof(a.Status)))
                        Notify(CustomerUsers(a.CustomerId), "booking-status", "Booking updated",
                            $"Your service booking is now {a.Status}. Open your bookings for details.", "Appointment", a.Id);
                    break;
                case WorkOrder w:
                    if (added)
                    {
                        Notify(Roles(SystemRole.Manager, SystemRole.Admin), "job-created", "Job ready for assignment",
                            $"Work order {w.WorkOrderNumber} is ready for review and mechanic assignment.", "WorkOrder", w.Id);
                        Notify(CustomerUsers(w.CustomerId), "job-created", "Workshop job created",
                            $"Work order {w.WorkOrderNumber} has been created for your vehicle.", "WorkOrder", w.Id);
                    }
                    else if (Changed(entry, nameof(w.Status)))
                    {
                        string status = w.Status switch {
                            WorkOrderStatus.InProgress => "in progress", WorkOrderStatus.WaitingForParts => "waiting for parts",
                            _ => w.Status.ToString().ToLowerInvariant()
                        };
                        Notify(CustomerUsers(w.CustomerId), "job-status", "Workshop progress updated",
                            $"Work order {w.WorkOrderNumber} is now {status}.", "WorkOrder", w.Id);
                        if (w.Status is WorkOrderStatus.Completed or WorkOrderStatus.WaitingForParts)
                            Notify(Roles(SystemRole.ServiceAdvisor, SystemRole.Manager, SystemRole.Admin), "job-status", "Workshop job updated",
                                $"Work order {w.WorkOrderNumber} is now {status}. Review its technician notes.", "WorkOrder", w.Id);
                    }
                    if (!added && Changed(entry, nameof(w.Description)))
                    {
                        const string marker = @"\[WF:HANDOVER\].*?\[/WF:HANDOVER\]";
                        var oldBlock = Regex.Match((string?)entry.Property(nameof(w.Description)).OriginalValue ?? "", marker, RegexOptions.Singleline).Value;
                        var newBlock = Regex.Match(w.Description ?? "", marker, RegexOptions.Singleline).Value;
                        if (newBlock.Length > 0 && newBlock != oldBlock)
                            Notify(CustomerUsers(w.CustomerId), "handover", "Vehicle handover recorded",
                                $"Handover details for work order {w.WorkOrderNumber} have been saved. Open your service history to read them.", "WorkOrder", w.Id);
                    }
                    break;
                case ModificationRequest m:
                    if (added || Changed(entry, nameof(m.Status)) || Changed(entry, nameof(m.ProposedCost)) || Changed(entry, nameof(m.AdvisorNotes)))
                    {
                        if (added || m.Status is ModificationRequestStatus.Approved or ModificationRequestStatus.Rejected or ModificationRequestStatus.Cancelled)
                            Notify(Roles(SystemRole.ServiceAdvisor, SystemRole.Admin), "modification", added ? "New modification request" : "Modification decision updated",
                                $"A vehicle modification request is {m.Status}. Open modifications to review it.", "ModificationRequest", m.Id);
                        if (!added)
                            Notify(CustomerUsers(m.CustomerId), "modification", "Modification request updated",
                                $"Your modification request is now {m.Status}. Open modifications to view the price and advisor notes.", "ModificationRequest", m.Id);
                    }
                    break;
                case PartRequisition r:
                    if (added)
                        Notify(Roles(SystemRole.Storekeeper, SystemRole.Admin), "parts", "New parts request",
                            $"A mechanic requested {r.QtyRequested} part(s). Open pending requisitions to review the request.", "PartRequisition", r.Id);
                    else if (Changed(entry, nameof(r.Status)) || Changed(entry, nameof(r.QtyReleased)))
                    {
                        var target = staff.Where(s => s.Id == r.RequestedByStaffId).Select(s => s.UserId);
                        Notify(target, "parts", "Parts request updated",
                            $"Your parts request is {r.Status}; {r.QtyReleased} of {r.QtyRequested} released.", "PartRequisition", r.Id);
                    }
                    break;
                case Estimate e:
                    if (!added && Changed(entry, nameof(e.Status)) && e.Status is EstimateStatus.Approved or EstimateStatus.Rejected)
                        Notify(Roles(SystemRole.ServiceAdvisor, SystemRole.Admin), "estimate-decision", "Customer estimate decision",
                            $"Estimate {e.EstimateNumber} has been {e.Status.ToString().ToLowerInvariant()} by the customer.", "Estimate", e.Id);
                    break;
                case EmergencyRequest e:
                    if (added)
                        Notify(Roles(SystemRole.ServiceAdvisor, SystemRole.Admin), "emergency", "New emergency request",
                            "A customer requested emergency assistance. Open emergency requests for details.", "EmergencyRequest", e.Id);
                    else if (Changed(entry, nameof(e.Status)))
                        Notify(CustomerUsers(e.CustomerId), "emergency", "Emergency request updated",
                            $"Your emergency request is now {e.Status}.", "EmergencyRequest", e.Id);
                    break;
                case QuoteRequest q:
                    if ((added || Changed(entry, nameof(q.Status))) && q.Status == QuoteRequestStatus.Open)
                        Notify(vendors.Select(v => v.UserId), "quote-request", "New quotation request",
                            $"Quotation request {q.RequestNumber} is open. Review the requested parts and submit your price.", "QuoteRequest", q.Id);
                    break;
                case VendorQuote q:
                    if (added || Changed(entry, nameof(q.Status)))
                        Notify(Roles(SystemRole.Manager, SystemRole.Storekeeper, SystemRole.Admin), "vendor-quote", "Vendor quotation submitted",
                            $"A vendor quotation is {q.Status}. Open vendor bid comparison to review it.", "VendorQuote", q.Id);
                    break;
                case PurchaseOrder o:
                    if (added || Changed(entry, nameof(o.Status)) || Changed(entry, nameof(o.ExpectedDeliveryAt)))
                    {
                        var quote = vendorQuotes.FirstOrDefault(q => q.Id == o.VendorQuoteId);
                        var target = vendors.Where(v => v.Id == quote?.VendorProfileId).Select(v => v.UserId);
                        Notify(target, "purchase-order", "Purchase order updated",
                            $"Purchase order {o.PurchaseOrderNumber} is {o.Status}. Open deliveries to review it.", "PurchaseOrder", o.Id);
                        Notify(Roles(SystemRole.Storekeeper, SystemRole.Manager, SystemRole.Admin), "purchase-order", "Purchase order updated",
                            $"Purchase order {o.PurchaseOrderNumber} is {o.Status}. Check receiving and delivery details.", "PurchaseOrder", o.Id);
                    }
                    break;
            }
        }
    }
}