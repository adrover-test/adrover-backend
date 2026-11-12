# Adrover Backend Architecture

## Overview
Adrover is a centralized backend serving both web and mobile applications for managing ride-hailing advertisements. Built with .NET 10, SQL Server, and EF Core 10, following N-tier architecture and SOLID principles.

## System Architecture

### N-Tier Layers
1. **API Layer** - ASP.NET Core REST API with minimal logic
2. **Service Layer** - Core business logic and orchestration
3. **Repository Layer** - Data access abstraction with EF Core
4. **Data Layer** - SQL Server with Entity Framework Core
5. **Infrastructure Layer** - External services (Azure, Razorpay, etc.)

### Key Features
- **Authentication**: JWT with refresh tokens
- **Authorization**: Role-based (SuperAdmin, Client, Driver)
- **Payment Processing**: Razorpay integration
- **File Storage**: Azure Blob Storage
- **Geolocation**: Google Maps API
- **Communication**: WhatsApp (Twilio), SMTP (Gmail/Outlook)
- **Notifications**: SignalR real-time updates
- **Logging**: Structured logging with Serilog
- **Exception Handling**: Centralized middleware

## Project Structure

```
Adrover.Backend/
├── src/
│   ├── Adrover.Api/                    # REST API Controllers
│   ├── Adrover.Application/            # Services & Business Logic
│   ├── Adrover.Domain/                 # Domain Models & Entities
│   ├── Adrover.Infrastructure/         # External Services & Repositories
│   └── Adrover.Data/                   # EF Core DbContext
├── ARCHITECTURE.md
├── API_DOCUMENTATION.md
├── SETUP_GUIDE.md
└── Adrover.Backend.sln
```

## Database Design

### Core Entities
- **Users** - Users with roles (SuperAdmin, Client, Driver)
- **Roles & Permissions** - Role-based authorization
- **Advertisements** - Ad campaigns with status tracking
- **Vehicles** - Registered vehicles for advertisement display
- **Payments** - Payment transactions and history
- **Wallets** - Client wallet balance management
- **Notifications** - User notifications and delivery tracking
- **AdPlays** - Advertisement play tracking and analytics
- **Enquiries** - WhatsApp enquiries and follow-ups
- **AuditLogs** - Activity logging for compliance

## Authentication & Authorization

### JWT Implementation
- Access tokens: 15 minutes expiry
- Refresh tokens: 7 days expiry
- Claims-based authorization
- Role and permission validation

### Roles
1. **SuperAdmin** - Full system control
2. **Client** - Advertisement management and billing
3. **Driver** - Ad viewing and wallet management

## External Integrations

### Razorpay
- Payment order creation and verification
- Webhook handling for payment confirmation
- Order and payment status tracking

### Azure Blob Storage
- Video and image uploads for ads
- Container-based organization by type
- SAS token generation for secure access

### Google Maps
- Route tracking for vehicles
- Location-based ad targeting
- Distance calculation for pricing

### WhatsApp (Twilio)
- Enquiry reception and routing
- Follow-up notifications
- Customer support messaging

### SMTP (Gmail/Outlook)
- Transactional email notifications
- Campaign status updates
- Invoice generation

## API Conventions

### URL Structure
```
/api/v1/{resource}
```

### Response Format
```json
{
  "success": true,
  "data": {},
  "message": "Operation successful",
  "timestamp": "2026-11-12T00:00:00Z"
}
```

### Error Format
```json
{
  "success": false,
  "error": {
    "code": "AUTH_001",
    "message": "Invalid credentials",
    "details": []
  },
  "timestamp": "2026-11-12T00:00:00Z"
}
```

## Security Practices

1. **Input Validation** - FluentValidation for all DTOs
2. **Secrets Management** - Configuration through environment variables
3. **HTTPS Only** - Enforced in production
4. **CORS** - Restricted to frontend origins
5. **Rate Limiting** - API throttling per endpoint
6. **SQL Injection Prevention** - Parameterized queries via EF Core
7. **XSS Prevention** - Input sanitization and output encoding
8. **CSRF Protection** - Token validation for state-changing operations

## Dependency Injection

All components registered in Startup:
- Services with scoped lifetime
- Repositories with scoped lifetime
- Infrastructure clients with singleton/transient as needed
- Configuration providers injected throughout

## Logging & Monitoring

- Structured logging with Serilog
- Console and file outputs
- Correlation IDs for request tracing
- Exception logging with stack traces
- Performance metrics collection

## Error Handling

Centralized exception middleware handling:
- AuthenticationException → 401
- AuthorizationException → 403
- ValidationException → 400
- BusinessLogicException → 422
- Generic exceptions → 500

## Testing Strategy

- No test projects included (per requirements)
- Manual testing via Postman/REST Client
- Frontend integration testing via Playwright
- Production monitoring and alerting

## Deployment

- Docker-ready (Dockerfile included)
- Azure App Service compatible
- SQL Server database with migrations
- Environment-based configuration
- Health check endpoints

## Configuration

Environment variables required:
- Database connection string
- JWT secret key
- Razorpay API keys
- Azure Blob Storage credentials
- Google Maps API key
- Twilio API keys
- SMTP credentials

See SETUP_GUIDE.md for complete configuration details.

## Performance Considerations

- Async/await throughout
- Database query optimization with includes
- Caching strategies for frequently accessed data
- Pagination for list endpoints
- Index optimization on hot queries

## Compliance & Audit

- Audit logging for sensitive operations
- GDPR-ready user data handling
- Payment compliance (PCI DSS considerations)
- Data retention policies
- Activity tracking
