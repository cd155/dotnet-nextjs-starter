using System.Net;

namespace SharedLibrary.Exceptions;

public class ServiceUnavailableException : AppException
{
    public ServiceUnavailableException(string? message = null, Exception? innerException = null)
        : base(message ?? ExceptionConstants.DefaultServiceUnavailableMessage, HttpStatusCode.ServiceUnavailable, ExceptionConstants.ServiceUnavailableTitle, innerException)
    {
    }
}
