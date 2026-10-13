using Microsoft.EntityFrameworkCore;
using ShiftDynamics.API.Domain.Entities;

namespace ShiftDynamics.API.Infrastructure.Data;

public partial class ShiftDynamicsDbContext
{
    // Called before the existing notification preparation. Business records and
    // their alerts are committed by the same SaveChanges operation.
    private async Task PrepareExtraAlertsAsync(CancellationToken ct)
    {
        ChangeTracker.DetectChanges();
        var entries=ChangeTracker.Entries()
            .Where(e=>e.State is EntityState.Added or EntityState.Modified)
            .Where(e=>e.Entity is InventoryItem or DiagnosticFinding or RepairAction).ToArray();
        if(entries.Length==0) return;
        var users=await Users.AsNoTracking().Where(u=>u.Status==AccountStatus.Active).ToListAsync(ct);
        var pending=ChangeTracker.Entries<Notification>().Where(e=>e.State==EntityState.Added)
            .Select(e=>e.Entity).ToList();
        void Add(IEnumerable<Guid> targets,string type,string title,string body,string entityType,Guid entityId)
        {
            foreach(var id in targets.Distinct())
            {
                if(pending.Any(n=>n.UserId==id && n.Type==type && n.EntityId==entityId && n.Body==body)) continue;
                var notification=new Notification { Id=Guid.NewGuid(), UserId=id, Type=type, Title=title,
                    Body=body, EntityType=entityType, EntityId=entityId, IsRead=false, CreatedAt=DateTime.UtcNow };
                Notifications.Add(notification);
                pending.Add(notification);
            }
        }
        var partIds=entries.Select(e=>e.Entity).OfType<InventoryItem>().Select(i=>i.PartId).Distinct().ToArray();
        var parts=partIds.Length==0?new List<Part>():await Parts.AsNoTracking().Where(p=>partIds.Contains(p.Id)).ToListAsync(ct);
        parts=parts.Concat(Parts.Local).GroupBy(p=>p.Id).Select(g=>g.Last()).ToList();
        var jobIds=entries.Select(e=>e.Entity).Select(e=>e switch {
            DiagnosticFinding d=>d.WorkOrderId, RepairAction r=>r.WorkOrderId, _=>Guid.Empty
        }).Where(id=>id!=Guid.Empty).Distinct().ToArray();
        var jobs=jobIds.Length==0?new List<WorkOrder>():await WorkOrders.AsNoTracking().Include(w=>w.Vehicle)
            .Where(w=>jobIds.Contains(w.Id)).ToListAsync(ct);
        jobs=jobs.Concat(WorkOrders.Local).GroupBy(w=>w.Id).Select(g=>g.Last()).ToList();
        var vehicleIds=jobs.Select(w=>w.VehicleId).Distinct().ToArray();
        var vehicles=vehicleIds.Length==0?new List<Vehicle>():await Vehicles.AsNoTracking()
            .Where(v=>vehicleIds.Contains(v.Id)).ToListAsync(ct);
        vehicles=vehicles.Concat(Vehicles.Local).GroupBy(v=>v.Id).Select(g=>g.Last()).ToList();
        foreach(var entry in entries)
        {
            bool added=entry.State==EntityState.Added;
            if(entry.Entity is InventoryItem item)
            {
                bool low=item.OnHandQty<=item.ReorderLevel;
                bool wasLow=!added && entry.Property(nameof(InventoryItem.OnHandQty)).OriginalValue is int oldQty
                    && entry.Property(nameof(InventoryItem.ReorderLevel)).OriginalValue is int oldLevel && oldQty<=oldLevel;
                // One alert when entering low stock; further low-stock saves do not repeat it.
                if(!low || wasLow) continue;
                var part=parts.FirstOrDefault(p=>p.Id==item.PartId);
                if(part==null) throw new InvalidOperationException("Cannot create a stock alert: part details are missing.");
                Add(users.Where(u=>u.Role is SystemRole.Storekeeper or SystemRole.Manager or SystemRole.Admin).Select(u=>u.Id),
                    "low-stock",LowStockTitle(part),LowStockBody(item,part),"InventoryItem",item.Id);
                continue;
            }
            var jobId=entry.Entity is DiagnosticFinding finding?finding.WorkOrderId:((RepairAction)entry.Entity).WorkOrderId;
            var job=jobs.FirstOrDefault(w=>w.Id==jobId);
            if(job==null) throw new InvalidOperationException("Cannot create a workshop alert: job details are missing.");
            var registration=vehicles.FirstOrDefault(v=>v.Id==job.VehicleId)?.RegistrationNumber;
            var prefix=job.WorkOrderNumber+(string.IsNullOrWhiteSpace(registration)?"":" ("+registration+")")+": ";
            var targets=users.Where(u=>u.Role is SystemRole.ServiceAdvisor or SystemRole.Manager or SystemRole.Admin
                || u.Role==SystemRole.Customer && u.CustomerId==job.CustomerId).Select(u=>u.Id);
            if(entry.Entity is DiagnosticFinding diagnostic)
            {
                bool changed=!added && (!Equals(entry.Property(nameof(DiagnosticFinding.Finding)).OriginalValue,diagnostic.Finding)
                    || !Equals(entry.Property(nameof(DiagnosticFinding.Severity)).OriginalValue,diagnostic.Severity));
                if(!added && !changed) continue;
                Add(targets,"diagnostic-finding",added?"New diagnostic finding":"Diagnostic finding updated",
                    prefix+diagnostic.Finding+(string.IsNullOrWhiteSpace(diagnostic.Severity)?"":" | Severity: "+diagnostic.Severity),
                    "WorkOrder",job.Id);
            }
            else if(entry.Entity is RepairAction repair)
            {
                bool changed=!added && (!Equals(entry.Property(nameof(RepairAction.Action)).OriginalValue,repair.Action)
                    || !Equals(entry.Property(nameof(RepairAction.Notes)).OriginalValue,repair.Notes));
                if(!added && !changed) continue;
                Add(targets,"repair-note",added?"New repair update":"Repair update edited",
                    prefix+repair.Action+(string.IsNullOrWhiteSpace(repair.Notes)?"":" | Notes: "+repair.Notes),"WorkOrder",job.Id);
            }
        }
    }

    private static string LowStockTitle(Part part)
    {
        var title="Low stock: "+part.Name;
        return title.Length>200?title[..200]:title;
    }

    private static string LowStockBody(InventoryItem item,Part part) =>
        $"{part.PartNumber} - {part.Name}: {item.OnHandQty} on hand; reorder level {item.ReorderLevel}."+
        (string.IsNullOrWhiteSpace(item.Location)?"":" Location: "+item.Location)+" Review stock and request replenishment.";

    // Installer calls this once for stock that was already low before installation.
    // Re-running it does not duplicate existing alerts, including read alerts.
    public async Task<int> InitializeExistingLowStockAlertsAsync(CancellationToken ct=default)
    {
        return await Database.CreateExecutionStrategy().ExecuteAsync(async()=> {
            ChangeTracker.Clear();
            await using var transaction=await Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,ct);
            var recipients=await Users.AsNoTracking().Where(u=>u.Status==AccountStatus.Active &&
                (u.Role==SystemRole.Manager || u.Role==SystemRole.Storekeeper || u.Role==SystemRole.Admin)).Select(u=>u.Id).ToListAsync(ct);
            var items=await InventoryItems.AsNoTracking().Include(i=>i.Part).Where(i=>i.OnHandQty<=i.ReorderLevel).ToListAsync(ct);
            int created=0;
            foreach(var item in items) foreach(var userId in recipients)
            {
                if(await Notifications.AnyAsync(n=>n.UserId==userId && n.Type=="low-stock" &&
                    n.EntityType=="InventoryItem" && n.EntityId==item.Id,ct)) continue;
                Notifications.Add(new Notification { Id=Guid.NewGuid(), UserId=userId, Type="low-stock",
                    Title=LowStockTitle(item.Part), Body=LowStockBody(item,item.Part), EntityType="InventoryItem",
                    EntityId=item.Id, IsRead=false, CreatedAt=DateTime.UtcNow });
                created++;
            }
            await SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return created;
        });
    }
}