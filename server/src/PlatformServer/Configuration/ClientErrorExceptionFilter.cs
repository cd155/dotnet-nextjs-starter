using Sentry.Extensibility;

namespace PlatformServer.Configuration;

/// <summary>
/// Keeps the exceptions <see cref="AppExceptionHandler"/> answered with a 4xx out of Sentry. The Sentry middleware
/// captures every exception the built-in exception handler sees, whatever status it was answered with. Only the
/// handler's mark counts, so the same exception types logged at Error elsewhere, as in the CMS, still reach Sentry.
/// </summary>
public sealed class ClientErrorExceptionFilter : IExceptionFilter
{
    public bool Filter(Exception ex) => ex.Data[AppExceptionHandler.AnsweredWithClientErrorKey] is true;
}
