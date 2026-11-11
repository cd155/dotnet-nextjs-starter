using System.Net;

namespace SharedLibrary.Exceptions;

/// <summary>
/// Exception thrown when validation errors occur.
/// Contains a collection of validation errors organized by field names.
/// </summary>
public class AppValidationException : AppException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AppValidationException"/> class.
    /// </summary>
    /// <param name="message">The error message. If not provided, uses the default validation message.</param>
    /// <param name="errors">A dictionary of validation errors where keys are field names and values are arrays of error messages.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public AppValidationException(string message = ExceptionConstants.DefaultValidationMessage, IDictionary<string, string[]>? errors = null, Exception? innerException = null)
        : base(message, HttpStatusCode.BadRequest, ExceptionConstants.ValidationErrorTitle, innerException)
    {
        Errors = errors ?? new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets the validation errors organized by field names.
    /// </summary>
    public IDictionary<string, string[]> Errors { get; }
}
