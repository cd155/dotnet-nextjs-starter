using System.Security.Claims;
using OpenIddict.Abstractions;
using SharedLibrary.Exceptions;

namespace PlatformServer.Configuration;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The signed-in user's id, as <see cref="FindUserId"/> reads it (the Identity cookie, or an MCP access token),
    /// without loading the user. The fallback policy already requires a signed-in user, so this throws only for a
    /// principal that carries no user id, which AppExceptionHandler answers with 401.
    /// </summary>
    public static string GetRequiredUserId(this ClaimsPrincipal user)
        => user.FindUserId() ?? throw new UnauthorizedException();

    /// <summary>
    /// The user's id from the Identity cookie's NameIdentifier claim, or from an MCP access token's <c>sub</c> claim,
    /// which carries no NameIdentifier. Null for a signed-out caller.
    /// </summary>
    public static string? FindUserId(this ClaimsPrincipal user)
        => user.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? user.FindFirstValue(OpenIddictConstants.Claims.Subject);
}
