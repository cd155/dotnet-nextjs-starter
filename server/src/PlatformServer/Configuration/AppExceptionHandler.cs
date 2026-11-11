using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using SharedLibrary.Exceptions;

namespace PlatformServer.Configuration;

/// <summary>
/// Answers the <c>SharedLibrary.Exceptions</c> types, and a request ASP.NET Core rejects, with their status and a
/// problem body written through <see cref="IProblemDetailsService"/>. Any other exception is left to the built-in
/// exception handler, which answers it with a 500.
/// </summary>
public sealed class AppExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<AppExceptionHandler> logger) : IExceptionHandler
{
    /// <summary>
    /// The <see cref="Exception.Data"/> key that marks an exception this handler answered with a 4xx, which
    /// <see cref="ClientErrorExceptionFilter"/> keeps out of Sentry.
    /// </summary>
    public const string AnsweredWithClientErrorKey = "PlatformServer.AnsweredWithClientError";

    /// <summary>
    /// The status this handler answers the exception with, or null when it leaves the exception to the built-in
    /// handler's 500.
    /// </summary>
    private static int? GetStatusCode(Exception exception) => exception switch
    {
        AppException appException => (int)appException.StatusCode,
        // A Minimal API argument that can't be bound (400), or a request Kestrel rejects, like an oversized
        // body (413).
        BadHttpRequestException badRequest => badRequest.StatusCode,
        _ => null
    };

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (GetStatusCode(exception) is not { } statusCode)
        {
            return false;
        }

        // The built-in handler doesn't log an exception an IExceptionHandler handles, so a 5xx is logged here at
        // Error, which Sentry's logger provider sends to Sentry.
        var isServerError = statusCode >= StatusCodes.Status500InternalServerError;
        logger.Log(isServerError ? LogLevel.Error : LogLevel.Warning, exception,
            "Exception handled for {Path} with status code {StatusCode}", httpContext.Request.Path, statusCode);

        if (!isServerError)
        {
            exception.Data[AnsweredWithClientErrorKey] = true;
        }

        httpContext.Response.StatusCode = statusCode;

        // A client whose Accept header rules out JSON gets the status without a body. Returning false here would
        // send the exception on to the built-in 500.
        await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = CreateProblemDetails(exception, statusCode),
            Exception = exception
        });

        return true;
    }

    private static ProblemDetails CreateProblemDetails(Exception exception, int statusCode) => exception switch
    {
        AppValidationException validationException => new HttpValidationProblemDetails(validationException.Errors)
        {
            Title = validationException.Title ?? ExceptionConstants.ValidationErrorTitle,
            Detail = validationException.Message,
            Status = statusCode
        },
        AppException appException => new ProblemDetails
        {
            Title = appException.Title ?? GetDefaultTitle(appException),
            Detail = appException.Message,
            Status = statusCode
        },
        _ => new ProblemDetails
        {
            Title = ReasonPhrases.GetReasonPhrase(statusCode),
            Detail = exception.Message,
            Status = statusCode
        }
    };

    private static string GetDefaultTitle(AppException exception) => exception switch
    {
        EntityNotFoundException => ExceptionConstants.ResourceNotFoundTitle,
        UnauthorizedException => ExceptionConstants.UnauthorizedTitle,
        ForbiddenException => ExceptionConstants.ForbiddenTitle,
        ServiceUnavailableException => ExceptionConstants.ServiceUnavailableTitle,
        ConflictException => ExceptionConstants.ConflictTitle,
        DomainException => ExceptionConstants.DomainErrorTitle,
        _ => ExceptionConstants.ApplicationErrorTitle
    };
}
