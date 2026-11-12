# Adrover Backend - Setup Guide

## Prerequisites

- .NET 10 LTS SDK
- SQL Server 2019 or later (or SQL Server Express)
- Visual Studio 2022 or VS Code
- Git for version control

## Installation Steps

### 1. Create the Solution Structure

```bash
cd C:\Users\ADMIN\source\repos\adrover-backend
dotnet new globaljson --sdk-version 10.0.201 --roll-forward latestFeature
dotnet new sln -n Adrover.Backend
```

### 2. Create Class Library Projects

```bash
# Domain Layer
dotnet new classlib -n Adrover.Domain -o src/Adrover.Domain

# Data Layer
dotnet new classlib -n Adrover.Data -o src/Adrover.Data

# Infrastructure Layer
dotnet new classlib -n Adrover.Infrastructure -o src/Adrover.Infrastructure

# Application/Service Layer
dotnet new classlib -n Adrover.Application -o src/Adrover.Application

# API Layer
dotnet new webapi -n Adrover.Api -o src/Adrover.Api
```

### 3. Add Projects to Solution

```bash
dotnet sln Adrover.Backend.sln add src/Adrover.Domain/Adrover.Domain.csproj
dotnet sln Adrover.Backend.sln add src/Adrover.Data/Adrover.Data.csproj
dotnet sln Adrover.Backend.sln add src/Adrover.Infrastructure/Adrover.Infrastructure.csproj
dotnet sln Adrover.Backend.sln add src/Adrover.Application/Adrover.Application.csproj
dotnet sln Adrover.Backend.sln add src/Adrover.Api/Adrover.Api.csproj
```

### 4. Add Project References

```bash
cd src/Adrover.Data
dotnet add reference ../Adrover.Domain/Adrover.Domain.csproj

cd ../Adrover.Infrastructure
dotnet add reference ../Adrover.Domain/Adrover.Domain.csproj
dotnet add reference ../Adrover.Data/Adrover.Data.csproj

cd ../Adrover.Application
dotnet add reference ../Adrover.Domain/Adrover.Domain.csproj
dotnet add reference ../Adrover.Infrastructure/Adrover.Infrastructure.csproj

cd ../Adrover.Api
dotnet add reference ../Adrover.Application/Adrover.Application.csproj
dotnet add reference ../Adrover.Infrastructure/Adrover.Infrastructure.csproj
```

### 5. Install NuGet Packages

#### Adrover.Domain
```bash
cd src/Adrover.Domain
dotnet add package System.Text.Json
```

#### Adrover.Data
```bash
cd src/Adrover.Data
dotnet add package Microsoft.EntityFrameworkCore --version 10.0.0
dotnet add package Microsoft.EntityFrameworkCore.SqlServer --version 10.0.0
dotnet add package Microsoft.EntityFrameworkCore.Tools --version 10.0.0
```

#### Adrover.Infrastructure
```bash
cd src/Adrover.Infrastructure
dotnet add package Azure.Storage.Blobs
dotnet add package Microsoft.Extensions.Http
dotnet add package Microsoft.Extensions.Configuration
dotnet add package Razorpay.Api
dotnet add package Twilio
dotnet add package MailKit
dotnet add package FluentValidation
dotnet add package Serilog
```

#### Adrover.Application
```bash
cd src/Adrover.Application
dotnet add package FluentValidation
dotnet add package Serilog
```

#### Adrover.Api
```bash
cd src/Adrover.Api
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer
dotnet add package System.IdentityModel.Tokens.Jwt
dotnet add package FluentValidation.AspNetCore
dotnet add package Serilog.AspNetCore
dotnet add package Serilog.Sinks.Console
dotnet add package Serilog.Sinks.File
```

## Environment Configuration

Create `appsettings.json` in Adrover.Api:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "System": "Warning"
    }
  },
  "Serilog": {
    "MinimumLevel": "Information",
    "WriteTo": [
      {
        "Name": "Console"
      },
      {
        "Name": "File",
        "Args": {
          "path": "logs/adrover-.txt",
          "rollingInterval": "Day"
        }
      }
    ]
  },
  "Jwt": {
    "SecretKey": "your-secret-key-min-32-characters-long-dev-only",
    "Issuer": "adrover.api",
    "Audience": "adrover.clients",
    "ExpirationMinutes": 15,
    "RefreshTokenExpirationDays": 7
  },
  "Database": {
    "ConnectionString": "Server=.;Database=AdRover;Trusted_Connection=true;TrustServerCertificate=true;"
  },
  "Razorpay": {
    "KeyId": "your-razorpay-key-id",
    "KeySecret": "your-razorpay-key-secret"
  },
  "AzureBlob": {
    "ConnectionString": "your-azure-connection-string",
    "ContainerNameVideos": "advertisement-videos",
    "ContainerNameImages": "advertisement-images"
  },
  "GoogleMaps": {
    "ApiKey": "your-google-maps-api-key"
  },
  "Twilio": {
    "AccountSid": "your-twilio-account-sid",
    "AuthToken": "your-twilio-auth-token",
    "PhoneNumber": "your-twilio-phone-number"
  },
  "Smtp": {
    "Host": "smtp.gmail.com",
    "Port": 587,
    "Username": "your-email@gmail.com",
    "Password": "your-app-password",
    "FromEmail": "noreply@adrover.com",
    "FromName": "Adrover"
  },
  "AllowedOrigins": [
    "http://localhost:5173",
    "http://localhost:5174",
    "https://yourdomain.com"
  ]
}
```

Create `appsettings.Development.json` (ignored by git, use for local development):

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug"
    }
  },
  "Jwt": {
    "SecretKey": "dev-secret-key-min-32-characters-long-must-be-secure"
  },
  "Database": {
    "ConnectionString": "Server=.;Database=AdRover;Trusted_Connection=true;TrustServerCertificate=true;"
  }
}
```

## Database Setup

### Option 1: Using SQL Server Express

1. Ensure SQL Server is running
2. Update connection string in appsettings.json
3. Create initial migration and database:

```bash
cd src/Adrover.Api
dotnet ef migrations add InitialCreate --project ../Adrover.Data
dotnet ef database update --project ../Adrover.Data
```

### Option 2: Using Docker SQL Server

```bash
docker run -e "ACCEPT_EULA=Y" -e "SA_PASSWORD=YourPassword@123" -p 1433:1433 -d mcr.microsoft.com/mssql/server:2022-latest

# Update connection string:
# Server=localhost,1433;Database=AdRover;User Id=sa;Password=YourPassword@123;TrustServerCertificate=true;
```

## Building the Solution

```bash
cd C:\Users\ADMIN\source\repos\adrover-backend
dotnet build Adrover.Backend.sln
```

## Running the API

```bash
cd src/Adrover.Api
dotnet run
```

API will be available at: `https://localhost:7000` or `http://localhost:5000`

## API Testing

### Using REST Client Extension (VS Code)

Create `test.http` in project root:

```http
### Register User
POST http://localhost:5000/api/v1/auth/register
Content-Type: application/json

{
  "email": "user@example.com",
  "phone": "919876543210",
  "password": "SecurePassword@123",
  "fullName": "John Doe",
  "role": "Client"
}

### Login
POST http://localhost:5000/api/v1/auth/login
Content-Type: application/json

{
  "email": "user@example.com",
  "password": "SecurePassword@123"
}
```

### Using Postman

1. Import the API collection
2. Set base URL: `http://localhost:5000`
3. Add auth token to header after login

## Secrets Management (Production)

Never commit secrets. Use these methods in production:

### Azure Key Vault
```csharp
builder.Configuration.AddAzureKeyVault(
    new Uri($"https://{keyVaultName}.vault.azure.net/"),
    new DefaultAzureCredential());
```

### Environment Variables
```bash
set JWT_SECRET_KEY=your-secure-key
set DATABASE_CONNECTION_STRING=your-connection-string
# Windows PowerShell: $env:VAR_NAME="value"
```

### User Secrets (Development Only)
```bash
dotnet user-secrets init
dotnet user-secrets set "Jwt:SecretKey" "your-secret-key"
dotnet user-secrets set "Database:ConnectionString" "your-connection-string"
```

## Database Migrations

### Create a Migration
```bash
dotnet ef migrations add MigrationName --project ../Adrover.Data --startup-project .
```

### Apply Migrations
```bash
dotnet ef database update --project ../Adrover.Data --startup-project .
```

### Remove Last Migration
```bash
dotnet ef migrations remove --project ../Adrover.Data --startup-project .
```

## Troubleshooting

### Database Connection Issues
- Verify SQL Server is running
- Check connection string format
- Ensure database exists or enable auto-creation

### NuGet Package Errors
- Clear NuGet cache: `dotnet nuget locals all --clear`
- Restore packages: `dotnet restore`

### Port Already in Use
- Change port in launchSettings.json
- Or kill process: `netstat -ano | findstr :5000`

### JWT Token Issues
- Verify secret key is >= 32 characters
- Check token expiration
- Validate claims in token

## Next Steps

1. Review [ARCHITECTURE.md](ARCHITECTURE.md) for design patterns
2. Check [API_DOCUMENTATION.md](API_DOCUMENTATION.md) for endpoint details
3. Implement features phase by phase as documented
4. Set up CI/CD pipeline for automated builds
5. Configure monitoring and alerting

## Production Deployment Checklist

- [ ] Use strong JWT secret key
- [ ] Configure all external API credentials
- [ ] Set up SQL Server backups
- [ ] Enable HTTPS with valid certificates
- [ ] Configure CORS for production domains
- [ ] Set up logging and monitoring
- [ ] Configure firewall rules
- [ ] Test all integrations in staging
- [ ] Set up health check monitoring
- [ ] Configure rate limiting
- [ ] Enable audit logging
- [ ] Set up automated backups
