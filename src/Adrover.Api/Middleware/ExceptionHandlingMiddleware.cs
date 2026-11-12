using System.Text.Json;
using Adrover.Domain.Exceptions;
using Adrover.Application.DTOs;
using Serilog;

namespace Adrover.Api.Middleware;

/// <summary>
/// Global exception handling middleware
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "An unhandled exception occurred");
            await HandleExceptionAsync(context, exception);
        }
    }

    private static Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        var response = exception switch
        {
            AuthenticationException authEx => new
            {
                statusCode = StatusCodes.Status401Unauthorized,
                errorResponse = new ApiErrorResponse
                {
                    Error = new ErrorDetails
                    {
                        Code = authEx.ErrorCode ?? "AUTH_ERROR",
                        Message = authEx.Message
                    }
                }
            },
            AuthorizationException authzEx => new
            {
                statusCode = StatusCodes.Status403Forbidden,
                errorResponse = new ApiErrorResponse
                {
                    Error = new ErrorDetails
                    {
                        Code = authzEx.ErrorCode ?? "AUTHZ_ERROR",
                        Message = authzEx.Message
                    }
                }
            },
            ResourceNotFoundException notFoundEx => new
            {
                statusCode = StatusCodes.Status404NotFound,
                errorResponse = new ApiErrorResponse
                {
                    Error = new ErrorDetails
                    {
                        Code = notFoundEx.ErrorCode ?? "NOT_FOUND",
                        Message = notFoundEx.Message
                    }
                }
            },
            ValidationException validEx => new
            {
                statusCode = StatusCodes.Status400BadRequest,
                errorResponse = new ApiErrorResponse
                {
                    Error = new ErrorDetails
                    {
                        Code = validEx.ErrorCode ?? "VALIDATION_ERROR",
                        Message = validEx.Message,
                        Details = validEx.Errors
                    }
                }
            },
            BusinessLogicException bizEx => new
            {
                statusCode = StatusCodes.Status422UnprocessableEntity,
                errorResponse = new ApiErrorResponse
                {
                    Error = new ErrorDetails
                    {
                        Code = bizEx.ErrorCode ?? "BUSINESS_ERROR",
                        Message = bizEx.Message,
                        Details = bizEx.Errors
                    }
                }
            },
            _ => new
            {
                statusCode = StatusCodes.Status500InternalServerError,
                errorResponse = new ApiErrorResponse
                {
                    Error = new ErrorDetails
                    {
                        Code = "INTERNAL_ERROR",
                        Message = "An internal server error occurred"
                    }
                }
            }
        };

        context.Response.StatusCode = response.statusCode;
        return context.Response.WriteAsJsonAsync(response.errorResponse);
    }
}

/// <summary>
/// Extension methods for middleware
/// </summary>
public static class MiddlewareExtensions
{
    public static IApplicationBuilder UseExceptionHandling(this IApplicationBuilder app)
    {
        return app.UseMiddleware<ExceptionHandlingMiddleware>();
    }
}
