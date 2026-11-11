using System.Net;

namespace SharedLibrary.Exceptions;

/// <summary>
/// Base class for all application-specific exceptions.
/// Provides consistent structure for exception handling across the application.
/// </summary>
public abstract class AppException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AppException"/> class.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="statusCode">The HTTP status code associated with this exception.</param>
    /// <param name="title">The title that describes the type of error. If null, a default title will be used.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    protected AppException(string message, HttpStatusCode statusCode, string? title = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Title = title;
    }

    /// <summary>
    /// Gets the HTTP status code associated with this exception.
    /// </summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>
    /// Gets the title that describes the type of error.
    /// </summary>
    public string? Title { get; }
}
