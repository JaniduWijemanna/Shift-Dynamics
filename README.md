# Shift Dynamics

### Garage & Auto Care Management System

Shift Dynamics is a full-stack garage and automotive service management system designed to manage vehicle servicing workflows, customer interactions, workshop operations, inventory, estimates, payments, and service records through a centralized platform.

## Key Features

- Customer registration and secure authentication
- Vehicle management
- Service appointment booking
- Work order management
- Mechanic and workshop workflow management
- Vehicle diagnostics
- Service estimates and customer approval
- Inventory and spare parts management
- Vendor and procurement management
- Invoice and payment management
- Vehicle modification requests
- Emergency assistance
- Service history tracking
- Notifications
- Management reports and dashboards
- Role-based access for different system users

## Technology Stack

| Area | Technologies |
|---|---|
| Backend | ASP.NET Core Web API, C# |
| Database | Microsoft SQL Server |
| ORM | Entity Framework Core |
| Frontend | HTML5, CSS3, JavaScript |
| Authentication | JWT Authentication |
| Password Security | BCrypt |
| Payments | PayHere Integration |
| Notifications | Firebase / Web Push |
| API Architecture | RESTful API |
| Development Tools | Visual Studio, VS Code |
| Version Control | Git & GitHub |

## System Architecture

The application follows a structured backend architecture separating responsibilities across:

- Controllers
- Application Services
- DTOs
- Domain Models
- Infrastructure
- Entity Framework Core DbContext
- Middleware

This structure helps keep business logic, database operations, API endpoints, and domain entities organized and maintainable.

## Database

The system uses Microsoft SQL Server with Entity Framework Core for database access and management.

The database supports major areas such as:

- Customers
- Vehicles
- Appointments
- Work Orders
- Mechanics
- Diagnostics
- Estimates
- Inventory
- Vendors
- Invoices
- Payments
- Modifications
- Notifications
- Service Records

Entity Framework Core migrations are used to maintain and update the database schema.

## Testing & Quality Assurance

The system was tested across major workflows including:

- Authentication and authorization
- Customer and vehicle operations
- Service booking
- Work order processing
- Estimate approval
- Workshop workflows
- Database operations
- API integration
- Payment-related workflows
- Role-based functionality

Testing was used to verify that the major modules communicate correctly across the frontend, backend, and database layers.

## My Contribution

My primary responsibilities in this project focused on **Database Administration and Quality Assurance**.

Key contributions included:

- Database design and configuration
- SQL Server database management
- Entity relationship analysis
- Database normalization
- Entity Framework Core database integration
- Database migration testing
- Data integrity verification
- Functional testing
- Integration testing
- Test case preparation and execution
- Identifying and resolving database-related issues
- Verifying end-to-end system workflows

## Running the Backend

Navigate to the API project:

```bash
cd backend/ShiftDynamics.API
```

Restore dependencies:

```bash
dotnet restore
```

Apply database migrations:

```bash
dotnet ef database update
```

Run the API:

```bash
dotnet run
```

The backend will start using the URL configured in the project launch settings.

## Security Configuration

Sensitive development credentials should not be stored directly in the repository.

For local development, ASP.NET Core User Secrets can be used for values such as the JWT signing key:

```bash
dotnet user-secrets set "Jwt:Key" "YOUR_LOCAL_SECRET_KEY"
```

Production credentials should be configured securely through the deployment environment.

## Project Status

Core application workflows are implemented and operational.

The system has been successfully built, connected to SQL Server, migrated using Entity Framework Core, and tested through the web interface and backend API.

---

**Janidu Dinsara Wijemanna**  
Software Developer | Computing & Software Engineering