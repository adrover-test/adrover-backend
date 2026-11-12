using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using FluentValidation;
using Adrover.Application.DTOs;
using Adrover.Application.DTOs.Auth;
using Adrover.Data.Repositories;
using Adrover.Infrastructure.Services;
using Adrover.Domain.Entities;
using Adrover.Domain.Exceptions;

namespace Adrover.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IUserRepository _userRepository;
    private readonly ITokenService _tokenService;
    private readonly IPasswordService _passwordService;
    private readonly IValidator<RegisterRequest> _registerValidator;
    private readonly IValidator<LoginRequest> _loginValidator;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IUserRepository userRepository,
        ITokenService tokenService,
        IPasswordService passwordService,
        IValidator<RegisterRequest> registerValidator,
        IValidator<LoginRequest> loginValidator,
        ILogger<AuthController> logger)
    {
        _userRepository = userRepository;
        _tokenService = tokenService;
        _passwordService = passwordService;
        _registerValidator = registerValidator;
        _loginValidator = loginValidator;
        _logger = logger;
    }

    [HttpPost("register")]
    public async Task<ActionResult<ApiResponse<UserDto>>> Register([FromBody] RegisterRequest request)
    {
        var validationResult = await _registerValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            var errors = validationResult.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
            var validationException = new FluentValidation.ValidationException(validationResult.Errors);
            throw validationException;
        }

        var existingUser = await _userRepository.GetByEmailAsync(request.Email);
        if (existingUser != null)
            throw new BusinessLogicException("Email already exists", "AUTH_001");

        var phoneUser = await _userRepository.GetByPhoneAsync(request.Phone);
        if (phoneUser != null)
            throw new BusinessLogicException("Phone number already exists", "AUTH_004");

        var roleEnum = request.Role switch
        {
            "Client" => UserRole.Client,
            "Driver" => UserRole.Driver,
            "SuperAdmin" => UserRole.SuperAdmin,
            _ => UserRole.Client
        };

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = request.Email,
            Phone = request.Phone,
            FullName = request.FullName,
            PasswordHash = _passwordService.HashPassword(request.Password),
            Role = roleEnum,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var newUser = await _userRepository.AddAsync(user);
        
        // Create wallet for clients
        if (roleEnum == UserRole.Client)
        {
            // Wallet creation would be handled by a wallet service
        }

        _logger.LogInformation("User registered: {Email}", request.Email);

        var userDto = MapToUserDto(newUser);
        return Ok(new ApiResponse<UserDto>
        {
            Success = true,
            Data = userDto,
            Message = "User registered successfully"
        });
    }

    [HttpPost("login")]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Login([FromBody] LoginRequest request)
    {
        var validationResult = await _loginValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            var validationException = new FluentValidation.ValidationException(validationResult.Errors);
            throw validationException;
        }

        var user = await _userRepository.GetByEmailAsync(request.Email);
        if (user == null)
            throw new AuthenticationException("Invalid email or password");

        if (!user.IsActive)
            throw new AuthenticationException("Account is disabled");

        if (!_passwordService.VerifyPassword(request.Password, user.PasswordHash))
            throw new AuthenticationException("Invalid email or password");

        var accessToken = _tokenService.GenerateAccessToken(user);
        var refreshToken = _tokenService.GenerateRefreshToken();

        var refreshTokenEntity = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Token = refreshToken,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };

        user.RefreshTokens.Add(refreshTokenEntity);
        user.LastLoginAt = DateTime.UtcNow;
        await _userRepository.UpdateAsync(user);

        _logger.LogInformation("User logged in: {Email}", request.Email);

        return Ok(new ApiResponse<AuthResponse>
        {
            Success = true,
            Data = new AuthResponse
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresIn = 900,
                TokenType = "Bearer"
            },
            Message = "Login successful"
        });
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> RefreshToken([FromBody] RefreshTokenRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            throw new AuthenticationException("Refresh token is required");

        var users = await _userRepository.FindAsync(u => u.RefreshTokens.Any(rt => rt.Token == request.RefreshToken && rt.IsActive));
        var user = users.FirstOrDefault();

        if (user == null)
            throw new AuthenticationException("Invalid or expired refresh token");

        var refreshTokenEntity = user.RefreshTokens.FirstOrDefault(rt => rt.Token == request.RefreshToken && rt.IsActive);
        if (refreshTokenEntity == null)
            throw new AuthenticationException("Invalid or expired refresh token");

        if (!user.IsActive)
            throw new AuthenticationException("User not found or inactive");

        var accessToken = _tokenService.GenerateAccessToken(user);

        return Ok(new ApiResponse<AuthResponse>
        {
            Success = true,
            Data = new AuthResponse
            {
                AccessToken = accessToken,
                ExpiresIn = 900,
                TokenType = "Bearer"
            },
            Message = "Token refreshed successfully"
        });
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<object>>> Logout()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userId, out var userGuid))
            throw new AuthenticationException("Invalid user ID");

        var user = await _userRepository.GetByIdAsync(userGuid);
        if (user != null)
        {
            user.RefreshTokens.Clear();
            await _userRepository.UpdateAsync(user);
        }

        return Ok(new ApiResponse<object>
        {
            Success = true,
            Message = "Logged out successfully"
        });
    }

    private UserDto MapToUserDto(User user)
    {
        return new UserDto
        {
            Id = user.Id,
            Email = user.Email,
            Phone = user.Phone,
            FullName = user.FullName,
            Role = user.Role.ToString(),
            ProfileImageUrl = user.ProfileImageUrl,
            IsActive = user.IsActive,
            IsEmailVerified = user.IsEmailVerified,
            IsPhoneVerified = user.IsPhoneVerified,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt,
            LastLoginAt = user.LastLoginAt
        };
    }
}
