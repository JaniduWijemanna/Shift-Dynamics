using Microsoft.EntityFrameworkCore;
using ShiftDynamics.API.Domain.Entities;

namespace ShiftDynamics.API.Infrastructure.Data;

public partial class ShiftDynamicsDbContext
{
    public DbSet<WebPushSubscription> WebPushSubscriptions => Set<WebPushSubscription>();
    public DbSet<WebPushDelivery> WebPushDeliveries => Set<WebPushDelivery>();
}