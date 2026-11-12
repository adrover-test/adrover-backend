namespace Adrover.Domain.Exceptions;

/// <summary>
/// Base exception for domain-level business logic violations
/// </summary>
public class BusinessLogicException : Exception
{
    public string? ErrorCode { get; set; }
    public Dictionary<string, string[]>? Errors { get; set; }

    public BusinessLogicException(string message, string? errorCode = null, Dictionary<string, string[]>? errors = null) 
        : base(message)
    {
        ErrorCode = errorCode;
        Errors = errors;
    }
}

/// <summary>
/// Exception thrown when authentication fails
/// </summary>
public class AuthenticationException : BusinessLogicException
{
    public AuthenticationException(string message) 
        : base(message, "AUTH_ERROR") { }
}

/// <summary>
/// Exception thrown when authorization fails
/// </summary>
public class AuthorizationException : BusinessLogicException
{
    public AuthorizationException(string message) 
        : base(message, "AUTHZ_ERROR") { }
}

/// <summary>
/// Exception thrown when a resource is not found
/// </summary>
public class ResourceNotFoundException : BusinessLogicException
{
    public ResourceNotFoundException(string message) 
        : base(message, "NOT_FOUND") { }
}

/// <summary>
/// Exception thrown when validation fails
/// </summary>
public class ValidationException : BusinessLogicException
{
    public ValidationException(string message, Dictionary<string, string[]>? errors = null) 
        : base(message, "VALIDATION_ERROR", errors) { }
}

/// <summary>
/// Exception thrown when a payment operation fails
/// </summary>
public class PaymentException : BusinessLogicException
{
    public PaymentException(string message, string? errorCode = null) 
        : base(message, errorCode ?? "PAYMENT_ERROR") { }
}

/// <summary>
/// Exception thrown when a file operation fails
/// </summary>
public class FileOperationException : BusinessLogicException
{
    public FileOperationException(string message, string? errorCode = null) 
        : base(message, errorCode ?? "FILE_ERROR") { }
}
