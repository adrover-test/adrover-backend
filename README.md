# Adrover Backend - Complete Setup & Development Guide

## Project Overview

Adrover Backend is a comprehensive, production-ready .NET 10 LTS REST API serving the Adrover platform for advertisement management in ride-hailing services. Built with Entity Framework Core 10, SQL Server, and following N-tier architecture and SOLID principles.

### Technology Stack

- **.NET 10 LTS** - Latest long-term support framework
- **SQL Server** - Enterprise-grade database
- **Entity Framework Core 10** - ORM for data access
- **ASP.NET Core** - Web framework
- **JWT** - Authentication tokens
- **Azure Blob Storage** - File management
- **Razorpay** - Payment processing
- **MailKit/SMTP** - Email communication
- **Serilog** - Structured logging
- **FluentValidation** - Input validation
- **BCrypt** - Password hashing

## Quick Start

### Prerequisites

- .NET 10 SDK (10.0.201 or later)
- SQL Server 2019+ or SQL Server Express
- Visual Studio 2022 or VS Code
- Git

### Installation

1. **Clone the repository**
```bash
cd C:\Users\ADMIN\source\repos\adrover-backend
```

2. **Restore dependencies**
```bash
dotnet restore
```

3. **Create database**
```bash
cd src/Adrover.Api
dotnet ef database update --project ../Adrover.Data
```

4. **Run the application**
```bash
cd src/Adrover.Api
dotnet run
```

API will be available at: `https://localhost:7000` or `http://localhost:5000`

## Project Structure

```
adrover-backend/
├── src/
│   ├── Adrover.Domain/
│   │   ├── Entities/          # Domain models
│   │   ├── Exceptions/        # Custom domain exceptions
│   │   └── *.cs              # Core domain logic
│   │
│   ├── Adrover.Data/
│   │   ├── Context/           # DbContext configuration
│   │   ├── Repositories/      # Data access layer
│   │   ├── Migrations/        # EF Core migrations
│   │   └── *.cs              # Data layer logic
│   │
│   ├── Adrover.Infrastructure/
│   │   ├── Services/          # External service integrations
│   │   ├── Integrations/      # API integrations
│   │   ├── Configuration/     # Infrastructure config
│   │   └── *.cs              # Infrastructure code
│   │
│   ├── Adrover.Application/
│   │   ├── DTOs/              # Data transfer objects
│   │   ├── Services/          # Business logic services
│   │   ├── Validators/        # FluentValidation rules
│   │   └── *.cs              # Application logic
│   │
│   └── Adrover.Api/
│       ├── Controllers/       # REST API endpoints
│       ├── Middleware/        # Custom middleware
│       ├── Configuration/     # Startup configuration
│       ├── Program.cs         # Application entry point
│       ├── appsettings.json   # Configuration
│       └── *.cs              # API layer code
│
├── Adrover.Backend.sln       # Solution file
├── ARCHITECTURE.md            # Architecture documentation
├── SETUP_GUIDE.md            # Detailed setup instructions
├── API_DOCUMENTATION.md      # Complete API reference
└── README.md                 # This file
```

## Configuration

### appsettings.json

Update `src/Adrover.Api/appsettings.json` with your configuration:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=.;Database=AdRover;Trusted_Connection=true;TrustServerCertificate=true;"
  },
  "Jwt": {
    "SecretKey": "your-32+-character-secret-key",
    "ExpirationMinutes": 15
  },
  "Razorpay": {
    "KeyId": "your-razorpay-key-id",
    "KeySecret": "your-razorpay-secret"
  },
  "AzureBlob": {
    "ConnectionString": "your-azure-connection-string"
  },
  "Smtp": {
    "Host": "smtp.gmail.com",
    "Username": "your-email@gmail.com",
    "Password": "your-app-password"
  }
}
```

### User Secrets (Development)

For sensitive data in development:

```bash
cd src/Adrover.Api
dotnet user-secrets init
dotnet user-secrets set "Jwt:SecretKey" "your-secret-key"
dotnet user-secrets set "Database:ConnectionString" "your-connection-string"
```

## Database Setup

### Create Initial Migration

```bash
cd src/Adrover.Api
dotnet ef migrations add InitialCreate --project ../Adrover.Data --output-dir Migrations
```

### Apply Migration

```bash
dotnet ef database update --project ../Adrover.Data
```

### Reset Database (Development Only)

```bash
dotnet ef database drop --project ../Adrover.Data
dotnet ef database update --project ../Adrover.Data
```

## Building & Testing

### Build Solution

```bash
cd C:\Users\ADMIN\source\repos\adrover-backend
dotnet build
```

### Run in Development

```bash
cd src/Adrover.Api
dotnet run --configuration Development
```

### Run in Release

```bash
cd src/Adrover.Api
dotnet run --configuration Release
```

### Watch Mode (Auto-reload on changes)

```bash
cd src/Adrover.Api
dotnet watch run
```

## API Endpoints

### Authentication

- `POST /api/v1/auth/register` - Register new user
- `POST /api/v1/auth/login` - Login user
- `POST /api/v1/auth/refresh` - Refresh access token
- `POST /api/v1/auth/logout` - Logout (requires auth)

### Users

- `GET /api/v1/users/me` - Get current user profile (requires auth)
- `PUT /api/v1/users/{id}` - Update user profile (requires auth)
- `POST /api/v1/users/{id}/change-password` - Change password (requires auth)

### Advertisements

- `GET /api/v1/advertisements` - List all ads
- `GET /api/v1/advertisements/{id}` - Get ad details
- `POST /api/v1/advertisements` - Create new ad (requires auth)
- `PUT /api/v1/advertisements/{id}` - Update ad (requires auth)
- `DELETE /api/v1/advertisements/{id}` - Delete ad (requires auth)

### Payments

- `POST /api/v1/payments/orders` - Create payment order (requires auth)
- `POST /api/v1/payments/verify` - Verify payment (requires auth)
- `GET /api/v1/payments/history` - Payment history (requires auth)
- `GET /api/v1/payments/{id}` - Payment details (requires auth)

### Wallets

- `GET /api/v1/wallets/balance` - Get wallet balance (requires auth)
- `POST /api/v1/wallets/topup` - Topup wallet (requires auth)
- `GET /api/v1/wallets/transactions` - Wallet transactions (requires auth)

### Notifications

- `GET /api/v1/notifications` - Get notifications (requires auth)
- `PUT /api/v1/notifications/{id}/read` - Mark as read (requires auth)
- `PUT /api/v1/notifications/read-all` - Mark all as read (requires auth)

### Admin

- `GET /api/v1/admin/approvals/pending` - Pending approvals (requires SuperAdmin)
- `POST /api/v1/admin/advertisements/{id}/approve` - Approve ad (requires SuperAdmin)
- `POST /api/v1/admin/advertisements/{id}/reject` - Reject ad (requires SuperAdmin)
- `GET /api/v1/admin/analytics` - System analytics (requires SuperAdmin)

### Health Check

- `GET /health` - API health status
- `GET /health/database` - Database connection status

## Authentication

### JWT Token Usage

Include token in request header:

```
Authorization: Bearer <access_token>
```

### Token Structure

Tokens include claims:
- `sub` (NameIdentifier) - User ID
- `email` - User email
- `name` - User full name
- `role` - User role (SuperAdmin, Client, Driver)
- `phone` - User phone

### Token Expiration

- Access Token: 15 minutes
- Refresh Token: 7 days

## Error Handling

All endpoints return standardized error responses:

```json
{
  "success": false,
  "error": {
    "code": "ERROR_CODE",
    "message": "Error description",
    "details": {}
  },
  "timestamp": "2026-11-12T10:30:00Z"
}
```

Common error codes:
- `AUTH_001` - Email already exists
- `AUTH_005` - User not found
- `AUTH_006` - Invalid password
- `NOT_FOUND` - Resource not found
- `VALIDATION_ERROR` - Input validation failed
- `BUSINESS_ERROR` - Business logic violation

## Logging

Logs are written to:
- Console output (development)
- `logs/` directory (rolling daily files)

Log format:
```
2026-11-12 10:30:45.123 +05:30 [INF] User logged in: user@example.com
```

## External Integrations

### Razorpay Payment Gateway

Configure in `appsettings.json`:
```json
"Razorpay": {
  "KeyId": "rzp_test_xxx",
  "KeySecret": "xxx"
}
```

### Azure Blob Storage

```json
"AzureBlob": {
  "ConnectionString": "DefaultEndpointsProtocol=https;..."
}
```

### Email/SMTP

```json
"Smtp": {
  "Host": "smtp.gmail.com",
  "Port": 587,
  "Username": "email@gmail.com",
  "Password": "app-password"
}
```

## Deployment

### Docker

Create `Dockerfile`:

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY src/Adrover.Api/bin/Release/net10.0/publish .
ENTRYPOINT ["dotnet", "Adrover.Api.dll"]
```

Build and run:
```bash
docker build -t adrover-api .
docker run -p 80:80 -e ASPNETCORE_ENVIRONMENT=Production adrover-api
```

### Azure App Service

```bash
# Login to Azure
az login

# Create resource group
az group create -n adrover-rg -l eastus

# Create App Service Plan
az appservice plan create -n adrover-plan -g adrover-rg --sku B1 --is-linux

# Create Web App
az webapp create -n adrover-api -g adrover-rg -p adrover-plan --runtime "DOTNETCORE|10"

# Deploy
dotnet publish -c Release
az webapp deployment source config-zip -g adrover-rg -n adrover-api --src bin/Release/net10.0/publish.zip
```

## Performance Optimization

1. **Database Indexing** - Configured on frequently queried fields
2. **Entity Eager Loading** - Using `.Include()` for related data
3. **Pagination** - Implemented on list endpoints
4. **Async/Await** - All I/O operations are async
5. **Connection Pooling** - Automatic with Entity Framework Core

## Security Best Practices

1. ✅ Password hashing with BCrypt
2. ✅ JWT authentication with secret key
3. ✅ Role-based authorization
4. ✅ HTTPS/TLS enforcement
5. ✅ CORS configuration
6. ✅ Input validation with FluentValidation
7. ✅ SQL injection prevention via EF Core
8. ✅ Centralized exception handling
9. ✅ Audit logging
10. ✅ Secrets not in configuration files

## Troubleshooting

### Database Connection Error

Ensure SQL Server is running and connection string is correct:

```powershell
# Windows Authentication
Server=.;Database=AdRover;Trusted_Connection=true;TrustServerCertificate=true;

# SQL Server Authentication
Server=localhost,1433;Database=AdRover;User Id=sa;Password=YourPassword;TrustServerCertificate=true;
```

### Port Already in Use

Change port in `launchSettings.json` or run:

```bash
dotnet run --urls "https://localhost:5001"
```

### JWT Secret Key Too Short

Secret key must be at least 32 characters. Example:
```
adrover-super-secret-key-min-32-characters-must-be-changed-in-production
```

### NuGet Restore Errors

```bash
dotnet nuget locals all --clear
dotnet restore --force
```

## Contributing

1. Create feature branch: `git checkout -b feature/xyz`
2. Commit changes: `git commit -am 'Add feature'`
3. Push to branch: `git push origin feature/xyz`
4. Create Pull Request

## Code Standards

- Follow C# naming conventions (PascalCase for public members)
- Use async/await for I/O operations
- Add XML comments for public methods
- Keep methods focused and small
- Use dependency injection
- Handle exceptions appropriately
- Write meaningful commit messages

## Support & Documentation

- API Documentation: [API_DOCUMENTATION.md](API_DOCUMENTATION.md)
- Architecture Guide: [ARCHITECTURE.md](ARCHITECTURE.md)
- Setup Instructions: [SETUP_GUIDE.md](SETUP_GUIDE.md)

## License

Proprietary - Adrover Technologies

## Contact

For support, contact: support@adrover.com
