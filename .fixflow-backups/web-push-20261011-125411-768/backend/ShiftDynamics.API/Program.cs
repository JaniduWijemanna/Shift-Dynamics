using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ShiftDynamics.API.Infrastructure.Data;
using ShiftDynamics.API.Application.Interfaces;
using ShiftDynamics.API.Middleware;
using ShiftDynamics.API.Application.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles)
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var errors = context.ModelState
                .Where(e => e.Value?.Errors.Count > 0)
                .ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value!.Errors.Select(e => e.ErrorMessage).ToArray());

            var payload = new ShiftDynamics.API.Common.ApiError
            {
                Success = false,
                Message = "One or more validation errors occurred.",
                Code = "validation_error",
                Errors = errors,
                TraceId = context.HttpContext.TraceIdentifier,
                Timestamp = DateTime.UtcNow
            };

            return new BadRequestObjectResult(payload);
        };
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Database connection string 'DefaultConnection' is not configured.");
}

builder.Services.AddDbContext<ShiftDynamicsDbContext>(options =>
    options.UseSqlServer(connectionString, sqlOptions =>
        sqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorNumbersToAdd: null)));

var jwtSection = builder.Configuration.GetSection("Jwt");
var jwtKey = jwtSection["Key"];
if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Length < 32)
    throw new InvalidOperationException("Jwt:Key must be configured securely and contain at least 32 characters.");
var jwtIssuer = jwtSection["Issuer"] ?? "ShiftDynamics";
var jwtAudience = jwtSection["Audience"] ?? "ShiftDynamicsClients";

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Customer", p => p.RequireRole("Customer"));
    options.AddPolicy("ServiceAdvisor", p => p.RequireRole("ServiceAdvisor", "Manager", "Admin"));
    options.AddPolicy("Manager", p => p.RequireRole("Manager", "Admin"));
    options.AddPolicy("Mechanic", p => p.RequireRole("Mechanic", "Manager", "Admin"));
    options.AddPolicy("Storekeeper", p => p.RequireRole("Storekeeper", "Manager", "Admin"));
    options.AddPolicy("Vendor", p => p.RequireRole("Vendor", "Admin"));
    options.AddPolicy("Staff", p => p.RequireRole("ServiceAdvisor", "Manager", "Mechanic", "Storekeeper", "Admin"));
});

builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ICustomerService, CustomerService>();
builder.Services.AddScoped<IVehicleService, VehicleService>();
builder.Services.AddScoped<IAppointmentService, AppointmentService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IModificationRequestService, ModificationRequestService>();
builder.Services.AddScoped<IProcurementService, ProcurementService>();
builder.Services.AddScoped<IServiceHistoryService, ServiceHistoryService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<IEstimateService, EstimateService>();
builder.Services.AddScoped<IInvoiceService, InvoiceService>();
builder.Services.AddScoped<IWorkOrderService, WorkOrderService>();
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<IContactService, ContactService>();
builder.Services.AddScoped<IEmergencyService, EmergencyService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<IManagerService, ManagerService>();
builder.Services.AddScoped<IMechanicService, MechanicService>();
builder.Services.AddScoped<IVendorService, VendorService>();
builder.Services.AddScoped<IHealthService, HealthService>();
builder.Services.AddScoped<IDatabaseService, DatabaseService>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendDev", policy =>
    {
        policy
            .WithOrigins(
                "http://localhost:3000",
                "http://localhost:5173",
                "http://localhost:5174",
                "http://localhost:5500",
                "http://127.0.0.1:5500",
                "http://localhost:8080")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Shift Dynamics API v1");
        options.RoutePrefix = "swagger";
    });
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseCors("FrontendDev");
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

if (app.Environment.IsDevelopment())
{
    app.MapGet("/", () => Results.Redirect("/swagger"));
}

app.Run();