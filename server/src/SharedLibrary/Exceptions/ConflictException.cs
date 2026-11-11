using System.Net;

namespace SharedLibrary.Exceptions;

/// <summary>
/// Exception thrown when a request can't be done in the current state, and could be once that state changes.
/// Represents a client error (409 Conflict).
/// </summary>
public class ConflictException : AppException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ConflictException"/> class.
    /// </summary>
    /// <param name="message">The error message. If not provided, uses the default conflict message.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public ConflictException(string? message = null, Exception? innerException = null)
        : base(message ?? ExceptionConstants.DefaultConflictMessage, HttpStatusCode.Conflict, ExceptionConstants.ConflictTitle, innerException)
    {
    }
}
