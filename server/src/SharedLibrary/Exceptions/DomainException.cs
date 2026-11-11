using System.Net;

namespace SharedLibrary.Exceptions;

/// <summary>
/// Exception thrown when a domain business rule is violated.
/// Represents a client error (400 Bad Request) indicating the request was malformed or violated business logic.
/// </summary>
public class DomainException : AppException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DomainException"/> class.
    /// </summary>
    /// <param name="message">The error message. If not provided, uses the default domain error message.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public DomainException(string? message = null, Exception? innerException = null)
        : base(message ?? ExceptionConstants.DefaultDomainMessage, HttpStatusCode.BadRequest, ExceptionConstants.DomainErrorTitle, innerException)
    {
    }
}
