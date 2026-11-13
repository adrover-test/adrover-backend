namespace Adrover.Application.Exceptions;

/// <summary>
/// Base exception for application layer
/// </summary>
public class ApplicationException : Exception
{
    public ApplicationException(string message) : base(message) { }
    public ApplicationException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Exception thrown when a resource is not found
/// </summary>
public class NotFoundException : ApplicationException
{
    public NotFoundException(string message) : base(message) { }
}

/// <summary>
/// Exception thrown when validation fails
/// </summary>
public class ValidationException : ApplicationException
{
    public ValidationException(string message) : base(message) { }
}

/// <summary>
/// Exception thrown when authentication fails
/// </summary>
public class AuthenticationException : ApplicationException
{
    public AuthenticationException(string message) : base(message) { }
}

/// <summary>
/// Exception thrown when authorization fails
/// </summary>
public class AuthorizationException : ApplicationException
{
    public AuthorizationException(string message) : base(message) { }
}
