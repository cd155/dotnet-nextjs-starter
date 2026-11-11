using System.Net;

namespace SharedLibrary.Exceptions;

/// <summary>
/// Exception thrown when a requested entity or resource is not found.
/// Represents a 404 Not Found HTTP response.
/// </summary>
public class EntityNotFoundException : AppException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EntityNotFoundException"/> class.
    /// </summary>
    /// <param name="message">The error message. If not provided, uses the default not found message.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public EntityNotFoundException(string? message = null, Exception? innerException = null)
        : base(message ?? ExceptionConstants.DefaultEntityNotFoundMessage, HttpStatusCode.NotFound, ExceptionConstants.ResourceNotFoundTitle, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EntityNotFoundException"/> class with specific entity information.
    /// </summary>
    /// <param name="entityName">The name of the entity type that was not found.</param>
    /// <param name="identifier">The identifier that was used to search for the entity.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public EntityNotFoundException(string entityName, object? identifier, Exception? innerException = null)
        : this($"{entityName} with identifier '{identifier}' was not found.", innerException)
    {
    }
}
