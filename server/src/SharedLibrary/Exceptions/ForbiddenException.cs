using System.Net;

namespace SharedLibrary.Exceptions;

public class ForbiddenException : AppException
{
    public ForbiddenException(string? message = null, Exception? innerException = null)
        : base(message ?? ExceptionConstants.DefaultForbiddenMessage, HttpStatusCode.Forbidden, ExceptionConstants.ForbiddenTitle, innerException)
    {
    }
}
