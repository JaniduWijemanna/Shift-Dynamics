using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ShiftDynamics.API.Domain.Entities;
using ShiftDynamics.API.Infrastructure.Data;

namespace ShiftDynamics.API.Application.Services;

// Local command only. No HTTP reset endpoint is installed.
public static class DemoDatabaseReset
{
    public static async Task RunAsync(ShiftDynamicsDbContext db, string expectedDatabase)
    {
        var connectionString = db.Database.GetConnectionString()
            ?? throw new InvalidOperationException("SQL connection string is missing.");
        await using var sql = new SqlConnection(connectionString);
        await sql.OpenAsync();
        if (!string.Equals(sql.Database, expectedDatabase, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(expectedDatabase, "ShiftDynamics", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Reset refused: the connected database must be ShiftDynamics.");
        Console.WriteLine($"Target SQL server: {sql.DataSource}; database: {sql.Database}");
        // Backup happens before any DELETE. Its failure stops the command.
        string backupPath;
        using (var command = sql.CreateCommand())
        {
            command.CommandText = "SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(4000));";
            var directory = Convert.ToString(await command.ExecuteScalarAsync());
            if (string.IsNullOrWhiteSpace(directory))
                throw new InvalidOperationException("SQL Server's default backup directory is unavailable. Configure it before resetting.");
            var separator = directory.Contains('/') ? "/" : "\\";
            backupPath = directory.TrimEnd('/', '\\') + separator +
                $"ShiftDynamics_before_demo_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.bak";
        }
        using (var command = sql.CreateCommand())
        {
            command.CommandTimeout = 600;
            var database = "[" + sql.Database.Replace("]", "]]") + "]";
            command.CommandText = $"BACKUP DATABASE {database} TO DISK = @path WITH COPY_ONLY, CHECKSUM; RESTORE VERIFYONLY FROM DISK = @path WITH CHECKSUM;";
            command.Parameters.Add("@path", SqlDbType.NVarChar, 4000).Value = backupPath;
            try { await command.ExecuteNonQueryAsync(); }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Backup or verification failed. No data was deleted. The SQL login needs backup/verification permissions and SQL Server needs access to its backup folder.", ex);
            }
        }
        Console.WriteLine("Verified backup on SQL SERVER: " + backupPath);
        await sql.CloseAsync();
        db.Database.SetCommandTimeout(300);
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync();
            // Order all application tables child-first using their real foreign keys.
            // Migration history, schema and constraints are preserved.
            await db.Database.ExecuteSqlRawAsync("""
                SET XACT_ABORT ON;
                DECLARE @tables TABLE (ObjectId int PRIMARY KEY, TableName nvarchar(600));
                INSERT INTO @tables
                SELECT t.object_id, QUOTENAME(s.name)+'.'+QUOTENAME(t.name)
                FROM sys.tables t JOIN sys.schemas s ON t.schema_id=s.schema_id
                WHERE t.is_ms_shipped=0 AND t.name<>'__EFMigrationsHistory';
                DECLARE @order TABLE (Sequence int IDENTITY, TableName nvarchar(600));
                DECLARE @id int, @name nvarchar(600), @statement nvarchar(max);
                WHILE EXISTS (SELECT 1 FROM @tables)
                BEGIN
                    SET @id=NULL;
                    SELECT TOP (1) @id=t.ObjectId, @name=t.TableName FROM @tables t
                    WHERE NOT EXISTS (
                        SELECT 1 FROM sys.foreign_keys f JOIN @tables child ON child.ObjectId=f.parent_object_id
                        WHERE f.referenced_object_id=t.ObjectId AND f.parent_object_id<>t.ObjectId
                    ) ORDER BY t.TableName;
                    IF @id IS NULL THROW 51000, 'Circular foreign keys detected. Reset cancelled; existing data is retained.', 1;
                    INSERT INTO @order(TableName) VALUES (@name);
                    DELETE FROM @tables WHERE ObjectId=@id;
                END;
                DECLARE @sequence int=1, @last int=(SELECT MAX(Sequence) FROM @order);
                WHILE @sequence<=@last
                BEGIN
                    SELECT @name=TableName FROM @order WHERE Sequence=@sequence;
                    SET @statement=N'DELETE FROM '+@name+N';';
                    EXEC sp_executesql @statement;
                    SET @sequence+=1;
                END;
                """);
            await SeedAsync(db);
            await ValidateAsync(db);
            await transaction.CommitAsync();
        });
        Console.WriteLine("RESET COMPLETE: six demo accounts and linked screenshot records created.");
    }

    private static async Task SeedAsync(ShiftDynamicsDbContext db)
    {
        var now = DateTime.UtcNow;
        var hash = BCrypt.Net.BCrypt.HashPassword("Demo@12345");
        var customer = new Customer { Id=Guid.NewGuid(), FirstName="Nimal", LastName="Perera",
            Phone="0771234567", Email="customer@shyftdynamics.test", Address="25 Lake Road, Colombo", CreatedAt=now.AddDays(-30) };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        var roles = new[] { SystemRole.Customer, SystemRole.ServiceAdvisor, SystemRole.Manager,
            SystemRole.Mechanic, SystemRole.Storekeeper, SystemRole.Vendor };
        var names = new[] { "Nimal Perera", "Sahan Silva", "Dinesh Fernando", "Kasun Jayasinghe", "Amal Wijesinghe", "Ruwan Auto Supplies" };
        var emails = new[] { "customer", "advisor", "manager", "mechanic", "storekeeper", "vendor" };
        var users = roles.Select((role,i) => new User { Id=Guid.NewGuid(), FullName=names[i],
            Email=emails[i]+"@shyftdynamics.test", Phone=i==0?customer.Phone:"077123456"+i, PasswordHash=hash,
            Role=role, Status=AccountStatus.Active, CustomerId=i==0?customer.Id:null,
            CreatedAt=now.AddDays(-30), UpdatedAt=now }).ToArray();
        db.Users.AddRange(users);
        await db.SaveChangesAsync();
        var staff = Enumerable.Range(1,4).Select(i => new Staff { Id=Guid.NewGuid(), UserId=users[i].Id,
            EmployeeNumber="SD-EMP-00"+i, Role=roles[i], Status=StaffStatus.Active,
            Specialization=i==3?"Engine, brakes and electrical diagnosis":roles[i].ToString(),
            CreatedAt=now.AddDays(-30), UpdatedAt=now }).ToArray();
        var mechanic = staff[2];
        db.Staff.AddRange(staff);
        var registration = new VendorRegistration { Id=Guid.NewGuid(), BusinessName="Ruwan Auto Supplies",
            ContactPerson="Ruwan De Silva", Mobile="0779876543", Email=users[5].Email,
            Address="18 Industrial Road, Colombo", Specialization="Engine and brake components", PasswordHash=hash,
            Status=VendorRegistrationStatus.Approved, SubmittedAt=now.AddDays(-25), ReviewedAt=now.AddDays(-24),
            ReviewedByUserId=users[2].Id, CreatedUserId=users[5].Id };
        db.VendorRegistrations.Add(registration);
        await db.SaveChangesAsync();
        var vendor = new VendorProfile { Id=Guid.NewGuid(), UserId=users[5].Id, RegistrationId=registration.Id,
            BusinessName=registration.BusinessName, ContactPerson=registration.ContactPerson, Mobile=registration.Mobile,
            Email=registration.Email, Address=registration.Address, Specialization=registration.Specialization,
            ApprovalStatus=VendorApprovalStatus.Active, ApprovedAt=now.AddDays(-24), CreatedAt=now.AddDays(-24), UpdatedAt=now };
        db.VendorProfiles.Add(vendor);
        var services = new[] { ("Full Service",8500m,180), ("Brake Inspection and Repair",4500m,120),
            ("Engine Diagnosis",3500m,90), ("Air Conditioning Service",6500m,120), ("Vehicle Modification",5000m,180) }
            .Select(s => new Service { Id=Guid.NewGuid(), Name=s.Item1, Description="Inspection and workshop service: "+s.Item1,
                BasePrice=s.Item2, EstimatedDurationMinutes=s.Item3, IsActive=true, CreatedAt=now.AddDays(-30), UpdatedAt=now }).ToArray();
        db.Services.AddRange(services);
        var specs = new[] { ("CAA-1001","Toyota","Corolla"), ("CBB-2002","Honda","Civic"),
            ("CAB-3003","Suzuki","Swift"), ("CAC-4004","Nissan","March"), ("CAD-5005","Toyota","Aqua"),
            ("CAE-6006","Honda","Fit"), ("CAF-7007","Mazda","Demio") };
        var vehicles = specs.Select((s,i) => new Vehicle { Id=Guid.NewGuid(), CustomerId=customer.Id,
            RegistrationNumber=s.Item1, Make=s.Item2, Model=s.Item3, Year=2015+i, Color=i%2==0?"Silver":"White",
            VIN="SDDEMO"+(10000000000L+i), CreatedAt=now.AddDays(-25) }).ToArray();
        db.Vehicles.AddRange(vehicles);
        var bays = new[] { "Bay 01 - General Repair", "Bay 02 - Diagnosis", "Bay 03 - Available" }
            .Select((name,i) => new WorkshopBay { Id=Guid.NewGuid(), Name=name,
                Status=i<2?BayStatus.Occupied:BayStatus.Available, CreatedAt=now.AddDays(-30), UpdatedAt=now }).ToArray();
        db.WorkshopBays.AddRange(bays);
        await db.SaveChangesAsync();
        var serviceIndices=new[] {0,1,2,3,0,3,1};
        var appointments = vehicles.Select((v,i) => new Appointment { Id=Guid.NewGuid(), CustomerId=customer.Id,
            VehicleId=v.Id, ServiceType=services[serviceIndices[i]].Name, AppointmentDate=now.Date.AddDays(i==0?1:-i).AddHours(4),
            Status=i>=4?AppointmentStatus.Completed:AppointmentStatus.Confirmed, Notes="Demo booking for "+v.RegistrationNumber,
            CreatedAt=now.AddDays(-15) }).ToList();
        appointments.Add(new Appointment { Id=Guid.NewGuid(), CustomerId=customer.Id, VehicleId=vehicles[0].Id,
            ServiceType=services[0].Name, AppointmentDate=now.Date.AddDays(3).AddHours(5),
            Status=AppointmentStatus.Scheduled, Notes="Upcoming service booking", CreatedAt=now });
        // The open job already arrived today; its appointment is not an upcoming booking.
        appointments[0].AppointmentDate=now.AddHours(-2);
        db.Appointments.AddRange(appointments);
        await db.SaveChangesAsync();
        var statuses = new[] { WorkOrderStatus.Open, WorkOrderStatus.Assigned, WorkOrderStatus.InProgress,
            WorkOrderStatus.WaitingForParts, WorkOrderStatus.Completed, WorkOrderStatus.Completed, WorkOrderStatus.Completed };
        var descriptions = new[] { "Full service and routine safety inspection.", "Brake noise when stopping; inspect pads and discs.",
            "Engine vibration and rough idle; diagnose ignition system.", "Cooling fan not operating; replacement relay required.",
            "Full service completed with oil and filter replacement.", "AC cooling restored after cleaning and gas recharge.",
            "Brake service completed; customer collecting vehicle after final payment." };
        var jobs = vehicles.Select((v,i) => new WorkOrder { Id=Guid.NewGuid(), CustomerId=customer.Id, VehicleId=v.Id,
            AppointmentId=appointments[i].Id, ServiceId=services[serviceIndices[i]].Id, AssignedStaffId=i==0?null:mechanic.Id,
            WorkOrderNumber="SD-DEMO-WO-00"+(i+1), Status=statuses[i],
            Description=descriptions[i]+"\n[WF:INSPECTION]"+JsonSerializer.Serialize(new {
                Body=true,Tires=true,Lights=true,Brakes=i!=1,Fluids=true,Interior=true,
                Notes=i==1?"Brake inspection required; customer reports squealing.":"Vehicle condition recorded on arrival.",RecordedAt=now.AddDays(-i) })+"[/WF:INSPECTION]",
            TechnicianNotes=i>=4?"Repairs completed. Road test and final safety checks passed.":i==2?"Ignition diagnosis in progress.":null,
            StartedAt=i>=2?now.AddDays(-i).AddHours(1):null,
            CompletedAt=i==4?now.AddHours(-1):i>4?now.AddDays(-(i-3)):null,
            CreatedAt=now.AddDays(-Math.Max(1,i)), UpdatedAt=now }).ToArray();
        jobs[4].Description+="\n[WF:HANDOVER]"+JsonSerializer.Serialize(new { WorkComplete=true,QualityChecked=true,
            CustomerInformed=true,PaymentConfirmed=true,DocumentsGiven=true,Notes="Vehicle collected. Next service due in 5,000 km.",RecordedAt=now.AddMinutes(-30) })+"[/WF:HANDOVER]";
        db.WorkOrders.AddRange(jobs);
        await db.SaveChangesAsync();
        foreach (var i in Enumerable.Range(1,6))
        {
            db.JobAssignments.Add(new JobAssignment { Id=Guid.NewGuid(), WorkOrderId=jobs[i].Id,
                MechanicStaffId=mechanic.Id, AssignedByUserId=users[2].Id, BayId=i==2?bays[0].Id:i==3?bays[1].Id:null,
                IsActive=i<4, AssignedAt=jobs[i].CreatedAt.AddMinutes(15), EndedAt=jobs[i].CompletedAt });
            if (i>=2)
            {
                db.DiagnosticFindings.Add(new DiagnosticFinding { Id=Guid.NewGuid(), WorkOrderId=jobs[i].Id,
                    MechanicStaffId=mechanic.Id, Finding=i==2?"Cylinder 2 ignition coil misfire; fault code P0302.":i==3?"Cooling fan relay failed; fan motor operates during direct test.":"Routine inspection identified worn consumables.",
                    Severity=i<4?"High":"Low", CreatedAt=jobs[i].CreatedAt.AddMinutes(30), UpdatedAt=now });
                db.RepairActions.Add(new RepairAction { Id=Guid.NewGuid(), WorkOrderId=jobs[i].Id,
                    MechanicStaffId=mechanic.Id, Action=i<4?"Initial inspection completed; Diagnosis completed":"Initial inspection completed; Diagnosis completed; Repair work completed; Parts installation checked; Final checks completed",
                    Notes=i==2?"Tested spark plugs and ignition wiring. Replacement coil requested.":i==3?"Removed failed relay; awaiting replacement part.":"Replaced worn items, verified operation and completed road test.",
                    CreatedAt=jobs[i].CreatedAt.AddHours(1), UpdatedAt=now });
                db.MechanicRecommendations.Add(new MechanicRecommendation { Id=Guid.NewGuid(), WorkOrderId=jobs[i].Id,
                    MechanicStaffId=mechanic.Id, Recommendation=i==2?"Replace faulty ignition coil and inspect remaining coils.":"Recheck fluid levels and return for the scheduled maintenance interval.",
                    Priority=i==2?"High":"Normal", CreatedAt=jobs[i].CreatedAt.AddHours(1), UpdatedAt=now });
                var start=jobs[i].StartedAt!.Value;
                db.LaborSessions.Add(new LaborSession { Id=Guid.NewGuid(), WorkOrderId=jobs[i].Id, MechanicStaffId=mechanic.Id,
                    StartedAt=start, EndedAt=start.AddMinutes(i<4?45:90), DurationSeconds=i<4?2700:5400, Status=LaborSessionStatus.Ended });
            }
        }
        var partSpecs = new[] { ("SD-P001","Engine Oil Filter","Filters",10,3,1800m),
            ("SD-P002","Front Brake Pads","Brakes",6,4,8500m), ("SD-P003","Ignition Coil","Electrical",1,3,12500m),
            ("SD-P004","Cooling Fan Relay","Electrical",0,2,2800m), ("SD-P005","Air Filter","Filters",12,4,2400m),
            ("SD-P006","Spark Plug","Ignition",3,8,1500m), ("SD-P007","Brake Fluid 500ml","Fluids",9,3,2200m),
            ("SD-P008","Wiper Blade Set","Accessories",7,2,3200m) };
        var parts=partSpecs.Select(p=>new Part { Id=Guid.NewGuid(), PartNumber=p.Item1, Name=p.Item2, Category=p.Item3,
            Description="Workshop replacement part: "+p.Item2, Compatibility="Common Japanese passenger vehicles",
            IsActive=true, CreatedAt=now.AddDays(-30), UpdatedAt=now }).ToArray();
        db.Parts.AddRange(parts);
        await db.SaveChangesAsync();
        var requisitions = new[] {
            new PartRequisition { Id=Guid.NewGuid(), WorkOrderId=jobs[2].Id, RequestedByStaffId=mechanic.Id, PartId=parts[2].Id, PartSpec="Ignition coil for Suzuki Swift", QtyRequested=1, Urgency=RequisitionUrgency.High, Reason="P0302 misfire repair", Status=RequisitionStatus.Pending, CreatedAt=now.AddHours(-2) },
            new PartRequisition { Id=Guid.NewGuid(), WorkOrderId=jobs[3].Id, RequestedByStaffId=mechanic.Id, PartId=parts[3].Id, PartSpec="12V cooling fan relay", QtyRequested=1, Urgency=RequisitionUrgency.Critical, Reason="Cooling fan not operating", Status=RequisitionStatus.Pending, CreatedAt=now.AddHours(-3) },
            new PartRequisition { Id=Guid.NewGuid(), WorkOrderId=jobs[1].Id, RequestedByStaffId=mechanic.Id, PartId=parts[1].Id, PartSpec="Front brake pad set for Honda Civic", QtyRequested=1, Urgency=RequisitionUrgency.Normal, Status=RequisitionStatus.Approved, ReviewedByUserId=users[4].Id, ReviewedAt=now.AddHours(-1), ReviewNotes="One set reserved; ready for issuing.", CreatedAt=now.AddHours(-4) },
            new PartRequisition { Id=Guid.NewGuid(), WorkOrderId=jobs[4].Id, RequestedByStaffId=mechanic.Id, PartId=parts[0].Id, PartSpec="Engine oil filter", QtyRequested=1, QtyReleased=1, Urgency=RequisitionUrgency.Normal, Status=RequisitionStatus.Released, ReviewedByUserId=users[4].Id, ReviewedAt=now.AddDays(-4), ReviewNotes="Issued for full service.", CreatedAt=now.AddDays(-4) }
        };
        db.PartRequisitions.AddRange(requisitions);
        await db.SaveChangesAsync();
        for (int i=0;i<parts.Length;i++)
        {
            db.InventoryItems.Add(new InventoryItem { Id=Guid.NewGuid(), PartId=parts[i].Id,
                OnHandQty=partSpecs[i].Item4, ReservedQty=i==1?1:0, ReorderLevel=partSpecs[i].Item5,
                UnitCost=partSpecs[i].Item6, Location="Rack "+(i/3+1)+" / Shelf "+(i%3+1), UpdatedAt=now });
            // Opening receipt plus actual demo procurement receipts minus issues = current on-hand.
            var opening=partSpecs[i].Item4+(i==0?1:0)-(i==4?4:0)-(i==7?2:0);
            if(opening>0) db.StockMovements.Add(new StockMovement { Id=Guid.NewGuid(), PartId=parts[i].Id,
                Type=StockMovementType.Receive, Quantity=opening, PerformedByUserId=users[4].Id,
                Reference="DEMO opening stock", CreatedAt=now.AddDays(-20) });
        }
        db.StockMovements.Add(new StockMovement { Id=Guid.NewGuid(), PartId=parts[0].Id, RequisitionId=requisitions[3].Id,
            Type=StockMovementType.Release, Quantity=1, PerformedByUserId=users[4].Id,
            Reference=jobs[4].WorkOrderNumber, CreatedAt=now.AddDays(-4) });
        await db.SaveChangesAsync();
        var requestParts=new[] {3,2,5,4,7};
        var requests=requestParts.Select((pi,i)=>new QuoteRequest { Id=Guid.NewGuid(), RequestNumber="SD-DEMO-RFQ-00"+(i+1),
            PartId=parts[pi].Id, PartRequisitionId=i==0?requisitions[1].Id:i==1?requisitions[0].Id:null,
            PartDescription=parts[pi].Name, Quantity=i==0?5:i==1?4:i==2?12:i==3?10:2,
            RequiredBy=now.AddDays(i==4?-1:3), Status=i<2?QuoteRequestStatus.Open:QuoteRequestStatus.Awarded,
            CreatedByUserId=users[4].Id, CreatedAt=now.AddDays(-7), UpdatedAt=now }).ToArray();
        db.QuoteRequests.AddRange(requests);
        await db.SaveChangesAsync();
        var quotes=Enumerable.Range(1,4).Select(i=>new VendorQuote { Id=Guid.NewGuid(), QuoteRequestId=requests[i].Id,
            VendorProfileId=vendor.Id, UnitPrice=partSpecs[requestParts[i]].Item6, AvailableQuantity=requests[i].Quantity+5,
            DeliveryDays=i==4?1:3, Notes="Genuine equivalent part. Delivery included in demo quotation.",
            Status=i==1?VendorQuoteStatus.Submitted:VendorQuoteStatus.Accepted, SubmittedAt=now.AddDays(-6), UpdatedAt=now }).ToArray();
        db.VendorQuotes.AddRange(quotes);
        await db.SaveChangesAsync();
        foreach(var i in Enumerable.Range(2,3))
        {
            var order=new PurchaseOrder { Id=Guid.NewGuid(), PurchaseOrderNumber="SD-DEMO-PO-00"+(i-1),
                VendorQuoteId=quotes[i-1].Id, QuoteRequestId=requests[i].Id, ApprovedByUserId=users[2].Id,
                Quantity=requests[i].Quantity, ReceivedQuantity=i==3?4:i==4?2:0, UnitPrice=quotes[i-1].UnitPrice,
                Status=i==2?PurchaseOrderStatus.Sent:i==3?PurchaseOrderStatus.PartiallyReceived:PurchaseOrderStatus.Received,
                ApprovedAt=now.AddDays(-5), ExpectedDeliveryAt=now.AddDays(i==4?-1:2), ReceivedAt=i==4?now.AddDays(-1):null };
            db.PurchaseOrders.Add(order);
            if(order.ReceivedQuantity>0) db.StockMovements.Add(new StockMovement { Id=Guid.NewGuid(), PartId=parts[requestParts[i]].Id,
                Type=StockMovementType.Receive, Quantity=order.ReceivedQuantity, PerformedByUserId=users[4].Id,
                Reference=order.PurchaseOrderNumber, CreatedAt=now.AddDays(-1) });
        }
        var estimates=new List<Estimate>();
        for(int i=0;i<jobs.Length;i++)
        {
            decimal labor=2500+i*500, partCost=i==2?12500:i==3?2800:i==4?1800:4500;
            var e=new Estimate { Id=Guid.NewGuid(), WorkOrderId=jobs[i].Id, EstimateNumber="SD-DEMO-EST-00"+(i+1),
                LaborCost=labor, PartsCost=partCost, Subtotal=labor+partCost, TotalAmount=labor+partCost,
                Status=i==0?EstimateStatus.Draft:i==1?EstimateStatus.Sent:EstimateStatus.Approved,
                CustomerApproved=i>=2, ApprovedAt=i>=2?jobs[i].CreatedAt.AddMinutes(20):null,
                Notes="Demo estimate in LKR. Labor and parts listed separately.", ExpiresAt=now.AddDays(7),
                CreatedAt=jobs[i].CreatedAt, UpdatedAt=now };
            estimates.Add(e);
        }
        db.Estimates.AddRange(estimates);
        await db.SaveChangesAsync();
        foreach(var i in new[] {4,5,6})
        {
            var e=estimates[i]; var paid=i==4?e.TotalAmount:i==6?3000m:0m;
            var invoice=new Invoice { Id=Guid.NewGuid(), WorkOrderId=jobs[i].Id, EstimateId=e.Id,
                InvoiceNumber="SD-DEMO-INV-00"+(i-3), LaborCost=e.LaborCost, PartsCost=e.PartsCost,
                Subtotal=e.Subtotal, TotalAmount=e.TotalAmount, AmountPaid=paid, BalanceDue=e.TotalAmount-paid,
                Status=i==4?InvoiceStatus.Paid:i==5?InvoiceStatus.Issued:InvoiceStatus.PartiallyPaid,
                IssuedAt=jobs[i].CompletedAt, DueDate=now.AddDays(7), Notes="Demonstration invoice; amounts in LKR.",
                CreatedAt=jobs[i].CreatedAt, UpdatedAt=now };
            db.Invoices.Add(invoice);
            if(paid>0) db.Payments.Add(new Payment { Id=Guid.NewGuid(), InvoiceId=invoice.Id,
                Amount=paid, Method=PaymentMethod.Cash, Status=PaymentStatus.Completed,
                TransactionReference="DEMO-CASH-00"+(i-3), Notes="Seeded demonstration cash payment; no real money transferred.",
                PaymentDate=jobs[i].CompletedAt!.Value, CreatedAt=jobs[i].CompletedAt!.Value });
        }
        db.ModificationRequests.AddRange(new ModificationRequest { Id=Guid.NewGuid(), CustomerId=customer.Id,
            VehicleId=vehicles[0].Id, RequestType="Exterior", Description="Fit a rear spoiler and add matching paint.",
            Status=ModificationRequestStatus.Quoted, ProposedCost=35000, AdvisorNotes="Includes fitting and painting; awaiting customer approval.", CreatedAt=now.AddDays(-2), UpdatedAt=now },
            new ModificationRequest { Id=Guid.NewGuid(), CustomerId=customer.Id, VehicleId=vehicles[1].Id,
                RequestType="Lighting", Description="Upgrade headlamps; retain road-legal beam alignment.",
                Status=ModificationRequestStatus.Submitted, CreatedAt=now.AddDays(-1), UpdatedAt=now });
        db.EmergencyRequests.Add(new EmergencyRequest { Id=Guid.NewGuid(), CustomerId=customer.Id, VehicleId=vehicles[0].Id,
            ProblemDescription="Vehicle will not start at home; battery assistance requested.", Location="25 Lake Road, Colombo", Latitude=6.927079m,
            Longitude=79.861244m, Status=EmergencyRequestStatus.Pending, RequestedAt=now.AddMinutes(-30), CreatedAt=now.AddMinutes(-30) });
        db.ContactInquiries.Add(new ContactInquiry { Id=Guid.NewGuid(), Name=customer.FirstName+" "+customer.LastName,
            Email=customer.Email, Phone=customer.Phone, Type="Service", Subject="Next service interval",
            Message="Please confirm the next service interval after the completed full service.", Status=InquiryStatus.New, CreatedAt=now.AddHours(-1) });
        foreach(var user in users) db.Notifications.Add(new Notification { Id=Guid.NewGuid(), UserId=user.Id,
            Type="demo", Title="Demo workshop records are ready", Body="Open your dashboard to view your linked demonstration records.",
            IsRead=false, CreatedAt=now });
        await db.SaveChangesAsync();
    }

    private static async Task ValidateAsync(ShiftDynamicsDbContext db)
    {
        var users=await db.Users.AsNoTracking().ToListAsync();
        if(users.Count!=6 || users.Select(u=>u.Role).Distinct().Count()!=6
            || users.Any(u=>!BCrypt.Net.BCrypt.Verify("Demo@12345",u.PasswordHash)))
            throw new InvalidOperationException("Demo account validation failed.");
        if(await db.Staff.CountAsync()!=4 || await db.VendorProfiles.CountAsync()!=1 || await db.WorkOrders.CountAsync()!=7)
            throw new InvalidOperationException("Demo relationship validation failed.");
        var inventory=await db.InventoryItems.AsNoTracking().ToListAsync();
        var movements=await db.StockMovements.AsNoTracking().ToListAsync();
        foreach(var item in inventory)
        {
            var balance=movements.Where(m=>m.PartId==item.PartId).Sum(m=>m.Type==StockMovementType.Release?-m.Quantity:m.Quantity);
            if(balance!=item.OnHandQty || item.ReservedQty>item.OnHandQty)
                throw new InvalidOperationException("Demo stock balance validation failed.");
        }
        var payments=await db.Payments.AsNoTracking().ToListAsync();
        foreach(var invoice in await db.Invoices.AsNoTracking().ToListAsync())
        {
            var paid=payments.Where(p=>p.InvoiceId==invoice.Id && p.Status==PaymentStatus.Completed).Sum(p=>p.Amount);
            if(paid!=invoice.AmountPaid || invoice.TotalAmount-paid!=invoice.BalanceDue)
                throw new InvalidOperationException("Demo invoice/payment validation failed.");
        }
        Console.WriteLine("Validated: 6 roles, 4 staff, 7 jobs, 8 stock items, 5 RFQs, 4 quotations, 3 purchase orders and 3 invoices.");
    }
}