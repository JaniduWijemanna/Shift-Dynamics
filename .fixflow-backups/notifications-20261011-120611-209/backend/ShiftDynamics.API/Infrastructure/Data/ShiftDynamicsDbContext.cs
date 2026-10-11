using Microsoft.EntityFrameworkCore;
using ShiftDynamics.API.Domain.Entities;

namespace ShiftDynamics.API.Infrastructure.Data;

public class ShiftDynamicsDbContext : DbContext
{
    public ShiftDynamicsDbContext(DbContextOptions<ShiftDynamicsDbContext> options)
        : base(options)
    {
    }

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Staff> Staff => Set<Staff>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();
    public DbSet<Estimate> Estimates => Set<Estimate>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<EmergencyRequest> EmergencyRequests => Set<EmergencyRequest>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<WorkshopBay> WorkshopBays => Set<WorkshopBay>();
    public DbSet<JobAssignment> JobAssignments => Set<JobAssignment>();
    public DbSet<LaborSession> LaborSessions => Set<LaborSession>();
    public DbSet<Part> Parts => Set<Part>();
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<PartRequisition> PartRequisitions => Set<PartRequisition>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<ContactInquiry> ContactInquiries => Set<ContactInquiry>();
    public DbSet<EmergencyServiceProvider> EmergencyServiceProviders => Set<EmergencyServiceProvider>();
    public DbSet<VendorRegistration> VendorRegistrations => Set<VendorRegistration>();
    public DbSet<VendorProfile> VendorProfiles => Set<VendorProfile>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<DiagnosticFinding> DiagnosticFindings => Set<DiagnosticFinding>();
    public DbSet<RepairAction> RepairActions => Set<RepairAction>();
    public DbSet<MechanicRecommendation> MechanicRecommendations => Set<MechanicRecommendation>();
    public DbSet<QuoteRequest> QuoteRequests => Set<QuoteRequest>();
    public DbSet<VendorQuote> VendorQuotes => Set<VendorQuote>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<ModificationRequest> ModificationRequests => Set<ModificationRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ShiftDynamicsDbContext).Assembly);
    }
}

