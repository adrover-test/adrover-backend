# Implementation Phases Guide

## Current Status: ✅ Phase 7 Complete - Core Infrastructure Ready

All foundational layers complete and compiling successfully. Ready for controller implementation.

---

## Phase 8: Controller Implementation (NEXT PHASE)

### Overview
Implement 7 controllers following AuthController template pattern. Each controller will:
- Inherit from ControllerBase
- Use dependency injection for repositories and services
- Implement proper async/await patterns
- Use FluentValidation validators
- Return standardized ApiResponse<T> wrapper
- Handle exceptions via centralized middleware

### Template Pattern (from AuthController)
```csharp
[ApiController]
[Route("api/[controller]")]
public class YourController : ControllerBase
{
    private readonly IYourService _service;
    private readonly ILogger<YourController> _logger;

    public YourController(IYourService service, ILogger<YourController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<YourDto>>> GetById(Guid id)
    {
        _logger.LogInformation("Getting item {id}", id);
        var result = await _service.GetByIdAsync(id);
        
        return Ok(new ApiResponse<YourDto>
        {
            Success = true,
            Data = result,
            Message = "Success message"
        });
    }
}
```

### Implementation Order (Recommended)
1. **UsersController** (Foundation for auth workflow)
2. **AdvertisementsController** (Core feature)
3. **PaymentsController** (Revenue critical)
4. **WalletsController** (Support feature)
5. **NotificationsController** (Engagement)
6. **FilesController** (Media handling)
7. **AdminController** (Platform management)

---

## Phase 8A: UsersController Implementation

### File Location
`src/Adrover.Api/Controllers/UsersController.cs`

### Endpoints to Implement

#### 1. GET /api/users/me
**Authentication**: Required (Bearer token)
```
Get current authenticated user profile
Returns: User details with sensitive info
```

#### 2. PUT /api/users/{id}
**Authentication**: Required
**Authorization**: Own profile or SuperAdmin
```
Update user profile (name, email, phone, photo)
Request: UpdateUserProfileRequest
Returns: Updated user details
```

#### 3. POST /api/users/{id}/change-password
**Authentication**: Required
```
Change user password
Request: ChangePasswordRequest (oldPassword, newPassword)
Returns: Success message
```

#### 4. POST /api/users/{id}/profile-picture
**Authentication**: Required
**Content-Type**: multipart/form-data
```
Upload user profile picture to Azure Blob
Request: File (image/jpeg, image/png, max 5MB)
Returns: File URL
```

### Required DTOs
```csharp
// Adrover.Application/DTOs/UserDtos.cs
public class UserProfileDto
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
    public string Phone { get; set; }
    public string Role { get; set; }
    public string ProfilePictureUrl { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class UpdateUserProfileRequest
{
    [Required]
    [StringLength(100)]
    public string Name { get; set; }
    
    [StringLength(500)]
    public string ProfilePictureUrl { get; set; }
}

public class ChangePasswordRequest
{
    [Required]
    public string OldPassword { get; set; }
    
    [Required]
    [StringLength(255, MinimumLength = 8)]
    public string NewPassword { get; set; }
}
```

### Required Service
```csharp
// Adrover.Application/Services/IUserService.cs
public interface IUserService
{
    Task<UserProfileDto> GetCurrentUserAsync(string userId);
    Task<UserProfileDto> UpdateProfileAsync(Guid userId, UpdateUserProfileRequest request);
    Task<bool> ChangePasswordAsync(Guid userId, ChangePasswordRequest request);
    Task<string> UploadProfilePictureAsync(Guid userId, Stream fileStream, string fileName);
}
```

### Service Implementation
```csharp
// Adrover.Application/Services/UserService.cs
public class UserService : IUserService
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordService _passwordService;
    private readonly IFileStorageService _fileStorageService;
    private readonly ILogger<UserService> _logger;

    public UserService(
        IUserRepository userRepository,
        IPasswordService passwordService,
        IFileStorageService fileStorageService,
        ILogger<UserService> logger)
    {
        _userRepository = userRepository;
        _passwordService = passwordService;
        _fileStorageService = fileStorageService;
        _logger = logger;
    }

    public async Task<UserProfileDto> GetCurrentUserAsync(string userId)
    {
        if (!Guid.TryParse(userId, out var userId_guid))
            throw new ValidationException("Invalid user ID format");

        var user = await _userRepository.GetByIdAsync(userId_guid);
        if (user == null)
            throw new NotFoundException("User not found");

        return MapToUserProfileDto(user);
    }

    public async Task<UserProfileDto> UpdateProfileAsync(Guid userId, UpdateUserProfileRequest request)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
            throw new NotFoundException("User not found");

        user.Name = request.Name;
        if (!string.IsNullOrEmpty(request.ProfilePictureUrl))
            user.ProfilePictureUrl = request.ProfilePictureUrl;

        var updated = await _userRepository.UpdateAsync(user);
        return MapToUserProfileDto(updated);
    }

    public async Task<bool> ChangePasswordAsync(Guid userId, ChangePasswordRequest request)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
            throw new NotFoundException("User not found");

        if (!_passwordService.VerifyPassword(request.OldPassword, user.PasswordHash))
            throw new AuthenticationException("Current password is incorrect");

        user.PasswordHash = _passwordService.HashPassword(request.NewPassword);
        await _userRepository.UpdateAsync(user);
        
        _logger.LogInformation("Password changed for user {userId}", userId);
        return true;
    }

    public async Task<string> UploadProfilePictureAsync(Guid userId, Stream fileStream, string fileName)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
            throw new NotFoundException("User not found");

        var fileUrl = await _fileStorageService.UploadFileAsync("user-profiles", $"{userId}/{fileName}", fileStream);
        
        user.ProfilePictureUrl = fileUrl;
        await _userRepository.UpdateAsync(user);
        
        return fileUrl;
    }

    private UserProfileDto MapToUserProfileDto(User user)
    {
        return new UserProfileDto
        {
            Id = user.Id,
            Name = user.Name,
            Email = user.Email,
            Phone = user.Phone,
            Role = user.Role.ToString(),
            ProfilePictureUrl = user.ProfilePictureUrl,
            CreatedAt = user.CreatedAt
        };
    }
}
```

### DI Registration
```csharp
// In ServiceConfiguration.cs, add:
services.AddScoped<IUserService, UserService>();
```

---

## Phase 8B: AdvertisementsController Outline

### Key Methods
- `GET /api/advertisements` - List with pagination & filters
- `GET /api/advertisements/{id}` - Details
- `POST /api/advertisements` - Create
- `PUT /api/advertisements/{id}` - Update
- `DELETE /api/advertisements/{id}` - Delete
- `POST /api/advertisements/{id}/submit-approval` - Submit for review
- `GET /api/advertisements/analytics` - Performance metrics

### Authorization Rules
- Clients: Can only access their own ads
- Drivers: Can only view ads for their assigned vehicle
- SuperAdmin: Full access to all ads

---

## Phase 8C: PaymentsController Outline

### Key Methods
- `POST /api/payments/create-order` - Create Razorpay order
- `POST /api/payments/verify` - Verify payment signature
- `GET /api/payments/history` - Transaction history
- `GET /api/payments/{id}` - Payment details
- `POST /api/payments/webhook` - Razorpay webhook

### Webhook Security
- Verify Razorpay signature in webhook
- Idempotency: Handle duplicate webhooks gracefully
- Logging: Log all webhook attempts

---

## Phase 8D-G: Other Controllers (Brief Overview)

### WalletsController
- Topup → Payment flow
- Deduct → On ad display
- Transaction tracking

### NotificationsController
- Email notifications via SMTP
- WhatsApp via Twilio
- In-app notifications

### FilesController
- Video upload to Azure Blob (max 500MB)
- Image upload (max 10MB)
- SAS URL generation for downloads

### AdminController
- Ad approval/rejection workflow
- Platform analytics
- User management
- Report generation

---

## Testing the Implementations

### Manual Testing Pattern (using test.http)
```http
### Create advertisement
POST http://localhost:5000/api/advertisements
Authorization: Bearer {accessToken}
Content-Type: application/json

{
  "name": "Test Ad",
  "brand": "TestBrand",
  "description": "Test Description",
  "targetArea": "Downtown",
  "videoUrl": "https://...",
  "budget": 5000
}

### Get advertisement
GET http://localhost:5000/api/advertisements/1
Authorization: Bearer {accessToken}
```

### Postman Collection
Export API_DOCUMENTATION.md as Postman collection for comprehensive testing.

---

## Development Workflow

### For Each Controller:
1. Create DTOs + Validators
2. Create Service Interface + Implementation
3. Register Service in DI container
4. Create Controller with endpoints
5. Add error handling (handled by middleware)
6. Add logging
7. Test with sample requests
8. Document in API_DOCUMENTATION.md

### Code Generation Helper
Most of the boilerplate can be generated:
- DTOs: Follow existing patterns in UserDtos.cs
- Services: Use UserService.cs as template
- Controllers: Use AuthController.cs as template
- Validators: Follow RequestValidators.cs pattern

---

## Build & Deploy After Each Phase

```bash
# After implementing each controller:
dotnet build
dotnet test (when test project added)

# Create migration if new entities added:
dotnet ef migrations add {MigrationName} -p src/Adrover.Data

# Apply migrations:
dotnet ef database update -p src/Adrover.Data
```

---

## Key Reminders

✅ Always use async/await
✅ Return ApiResponse<T> wrapper
✅ Log important operations
✅ Validate input with FluentValidation
✅ Handle exceptions via middleware
✅ Use dependency injection
✅ No hardcoded secrets
✅ Follow SOLID principles
✅ Comment complex logic only
✅ Maintain consistent naming conventions

---

## Estimated Timeline (if implementing sequentially)

- Phase 8A (Users): 2-3 hours
- Phase 8B (Advertisements): 3-4 hours
- Phase 8C (Payments): 2-3 hours
- Phase 8D (Wallets): 1-2 hours
- Phase 8E (Notifications): 1-2 hours
- Phase 8F (Files): 1-2 hours
- Phase 8G (Admin): 2-3 hours

**Total: ~15-19 hours to complete all controllers**

After controllers, remaining phases:
- Phase 9: Webhook handlers & background jobs
- Phase 10: Rate limiting & caching
- Phase 11: Deployment to Azure
- Phase 12: Load testing & optimization
