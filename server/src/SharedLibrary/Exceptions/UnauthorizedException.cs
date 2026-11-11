using System.Net;

namespace SharedLibrary.Exceptions;

public class UnauthorizedException : AppException
{
    public UnauthorizedException(string? message = null, Exception? innerException = null)
        : base(message ?? ExceptionConstants.DefaultUnauthorizedMessage, HttpStatusCode.Unauthorized, ExceptionConstants.UnauthorizedTitle, innerException)
    {
    }
}
