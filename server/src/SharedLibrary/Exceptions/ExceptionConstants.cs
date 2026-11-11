namespace SharedLibrary.Exceptions;

/// <summary>
/// Contains constant values for exception messages and titles to ensure consistency across the application.
/// </summary>
public static class ExceptionConstants
{
    // Default error messages
    public const string DefaultValidationMessage = "One or more validation errors occurred.";
    public const string DefaultDomainMessage = "A domain rule was violated.";
    public const string DefaultEntityNotFoundMessage = "The requested resource was not found.";
    public const string DefaultForbiddenMessage = "You do not have permission to perform this action.";
    public const string DefaultUnauthorizedMessage = "Authentication is required to access this resource.";
    public const string DefaultServiceUnavailableMessage = "This site can't be reached.";
    public const string DefaultConflictMessage = "The request can't be done in the current state.";
    public const string DefaultGeneralErrorMessage = "An error occurred while processing the request.";

    // Default titles
    public const string ValidationErrorTitle = "Validation error";
    public const string DomainErrorTitle = "Domain error";
    public const string ResourceNotFoundTitle = "Resource not found";
    public const string ForbiddenTitle = "Forbidden";
    public const string UnauthorizedTitle = "Unauthorized";
    public const string ServiceUnavailableTitle = "Service unavailable error";
    public const string ConflictTitle = "Conflict";

    // Factory fallback messages
    public const string FactoryDomainFallback = "A domain error occurred.";
    public const string FactoryEntityNotFoundFallback = "Entity not found.";
    public const string FactoryForbiddenFallback = "Forbidden.";
    public const string FactoryUnauthorizedFallback = "Unauthorized.";
    public const string FactoryServiceUnavailableFallback = "Service unavailable.";

    // Middleware fallback
    public const string ApplicationErrorTitle = "Application error";
}
